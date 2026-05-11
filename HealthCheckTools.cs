using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace MediahostHealthMCP;

[McpServerToolType]
public sealed class HealthCheckTools
{
    private readonly DbService    _db;
    private readonly IConfiguration _config;
    private readonly EmailService _email;

    public HealthCheckTools(DbService db, IConfiguration config, EmailService email)
    {
        _db     = db;
        _config = config;
        _email  = email;
    }

    private string QueriesPath =>
        _config["QUERIES_FILE"] ?? Path.Combine(AppContext.BaseDirectory, "queries.yaml");

    // -----------------------------------------------------------------------

    [McpServerTool, Description(
        "Returns the names and descriptions of all available health checks. " +
        "Call this first so you know what checks exist before running them.")]
    public string ListChecks()
    {
        var checks = QueryLoader.Load(QueriesPath)
            .Select(c => new { c.Name, c.Description, c.PassCondition })
            .ToList();

        return JsonSerializer.Serialize(checks, JsonOptions.Default);
    }

    // -----------------------------------------------------------------------

    [McpServerTool, Description(
        "Runs a single named health-check query and returns the result. " +
        "Args: check_name — the Name value from ListChecks.")]
    public async Task<string> RunCheck(
        [Description("The name of the check as listed in queries.yaml")]
        string checkName)
    {
        var checks = QueryLoader.Load(QueriesPath);
        var check  = checks.FirstOrDefault(c => c.Name == checkName);

        if (check is null)
        {
            return Serialize(new CheckResult
            {
                Name  = checkName,
                Status = "error",
                Error  = $"No check named '{checkName}' found in queries.yaml",
                RanAt  = UtcNow(),
            });
        }

        return Serialize(await ExecuteCheck(check));
    }

    // -----------------------------------------------------------------------

    [McpServerTool, Description(
        "Runs every health check defined in queries.yaml and returns a full summary report. " +
        "Use this for the morning report routine.")]
    public async Task<string> RunAllChecks()
    {
        var checks  = QueryLoader.Load(QueriesPath);
        var results = new List<CheckResult>();

        foreach (var check in checks)
            results.Add(await ExecuteCheck(check));

        var summary = new ReportSummary
        {
            RanAt   = UtcNow(),
            Total   = results.Count,
            Passing = results.Count(r => r.Status == "pass"),
            Failing = results.Count(r => r.Status == "fail"),
            Errors  = results.Count(r => r.Status == "error"),
            Checks  = results,
        };

        return Serialize(summary);
    }

    // -----------------------------------------------------------------------

    [McpServerTool, Description(
        "Sends the latest health check report via email to the configured recipient. " +
        "First run RunAllChecks, then call this with the report JSON to send it.")]
    public async Task<string> SendReportEmail(
        [Description("The JSON report from RunAllChecks")]
        string reportJson)
    {
        try
        {
            var report = JsonSerializer.Deserialize<ReportSummary>(reportJson, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });

            if (report is null)
            {
                return Serialize(new { status = "error", message = "Failed to parse report JSON" });
            }

            await _email.SendReportAsync(report);

            return Serialize(new { status = "ok", message = "Report sent successfully" });
        }
        catch (Exception ex)
        {
            return Serialize(new { status = "error", message = ex.Message });
        }
    }

    // -----------------------------------------------------------------------

    [McpServerTool, Description(
        "Runs all health checks and sends the report via email in a single operation. " +
        "Perfect for scheduled health check reports.")]
    public async Task<string> RunChecksAndEmailReport()
    {
        try
        {
            var checks  = QueryLoader.Load(QueriesPath);
            var results = new List<CheckResult>();

            foreach (var check in checks)
                results.Add(await ExecuteCheck(check));

            var summary = new ReportSummary
            {
                RanAt   = UtcNow(),
                Total   = results.Count,
                Passing = results.Count(r => r.Status == "pass"),
                Failing = results.Count(r => r.Status == "fail"),
                Errors  = results.Count(r => r.Status == "error"),
                Checks  = results,
            };

            await _email.SendReportAsync(summary);

            return Serialize(new { 
                status = "ok", 
                message = "Health checks completed and report emailed successfully",
                report = summary
            });
        }
        catch (Exception ex)
        {
            return Serialize(new { status = "error", message = ex.Message });
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<CheckResult> ExecuteCheck(HealthCheck check)
    {
        try
        {
            var value  = await _db.QueryScalarAsync(check.Query);
            var status = ConditionEvaluator.Evaluate(check.PassCondition, value);
            var failMessage = "";

            if (status == "fail")
            {
                failMessage = check.FailMessage;

                // For time_since checks, format the time difference and threshold
                if (check.PassCondition.StartsWith("time_since:"))
                {
                    try
                    {
                        var parts = check.PassCondition.Split(':');
                        var unit = parts[1];
                        var threshold = double.Parse(parts[2]);

                        // Parse the datetime from value
                        DateTime targetTime;
                        if (value is DateTime dt)
                        {
                            targetTime = dt;
                        }
                        else if (double.TryParse(value?.ToString(), out var unixTimestamp))
                        {
                            targetTime = DateTime.UnixEpoch.AddSeconds(unixTimestamp);
                        }
                        else if (value != null)
                        {
                            targetTime = DateTime.Parse(value.ToString()!);
                        }
                        else
                        {
                            throw new InvalidOperationException("time_since check returned null value");
                        }

                        var timeDiff = DateTime.UtcNow - targetTime;
                        var formattedDiff = ConditionEvaluator.FormatTimeDifference(timeDiff);
                        var formattedThreshold = ConditionEvaluator.FormatThreshold(unit, threshold);

                        failMessage = failMessage
                            .Replace("{value}", formattedDiff)
                            .Replace("{threshold}", formattedThreshold);
                    }
                    catch
                    {
                        // If formatting fails, just use the raw value
                        failMessage = failMessage.Replace("{value}", value?.ToString() ?? "");
                    }
                }
                else
                {
                    // For numeric checks, just replace {value} with the raw value
                    failMessage = failMessage.Replace("{value}", value?.ToString() ?? "");
                }
            }

            return new CheckResult
            {
                Name        = check.Name,
                Description = check.Description,
                Status      = status,
                Value       = value,
                FailMessage = status == "fail" ? failMessage : null,
                RanAt       = UtcNow(),
            };
        }
        catch (Exception ex)
        {
            return new CheckResult
            {
                Name  = check.Name,
                Description = check.Description,
                Status = "error",
                Error  = ex.Message,
                RanAt  = UtcNow(),
            };
        }
    }

    private static string UtcNow() =>
        DateTime.UtcNow.ToString("o");

    private static string Serialize(object obj) =>
        JsonSerializer.Serialize(obj, JsonOptions.Default);
}

// Shared JSON options
file static class JsonOptions
{
    internal static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented               = true,
    };
}
