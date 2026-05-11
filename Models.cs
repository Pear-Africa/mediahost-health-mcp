using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MediahostHealth;

// ---------------------------------------------------------------------------
// YAML models
// ---------------------------------------------------------------------------

public sealed class QueryFile
{
    public List<HealthCheck> Checks { get; set; } = [];
}

public sealed class HealthCheck
{
    public string Name           { get; set; } = "";
    public string Description    { get; set; } = "";
    public string Query          { get; set; } = "";
    [YamlMember(Alias = "pass_condition")]
    public string PassCondition  { get; set; } = "";
    [YamlMember(Alias = "fail_message")]
    public string FailMessage    { get; set; } = "";
}

// ---------------------------------------------------------------------------
// Result models
// ---------------------------------------------------------------------------

public sealed class CheckResult
{
    public string  Name        { get; init; } = "";
    public string  Description { get; init; } = "";
    public string  Status      { get; init; } = "";   // pass | fail | error
    public object? Value       { get; init; }
    public string? FailMessage { get; init; }
    public string? Error       { get; init; }
    public string  RanAt       { get; init; } = "";
}

public sealed class ReportSummary
{
    public string         RanAt    { get; init; } = "";
    public int            Total    { get; init; }
    public int            Passing  { get; init; }
    public int            Failing  { get; init; }
    public int            Errors   { get; init; }
    public List<CheckResult> Checks { get; init; } = [];
}

// ---------------------------------------------------------------------------
// YAML loader — reloads on every call so adding checks needs no restart
// ---------------------------------------------------------------------------

public static class QueryLoader
{
    private static readonly IDeserializer Deserializer =
        new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

    public static List<HealthCheck> Load(string path)
    {
        var yaml = File.ReadAllText(path);
        return Deserializer.Deserialize<QueryFile>(yaml).Checks;
    }
}

// ---------------------------------------------------------------------------
// Pass-condition evaluator
// ---------------------------------------------------------------------------

public static class ConditionEvaluator
{
    /// <summary>
    /// Operators: eq:0  neq:0  gt:0  gte:1  lt:100  lte:99  between:1:10
    /// </summary>
    public static string Evaluate(string condition, object? value)
    {
        try
        {
            if (value is null) return "fail";

            var parts = condition.Split(':');
            var op    = parts[0];

            // Time-based checks
            if (op == "time_since")
            {
                try
                {
                    // Parse the datetime from value
                    DateTime targetTime;
                    if (value is DateTime dt)
                    {
                        targetTime = dt;
                    }
                    else if (double.TryParse(value.ToString(), out var unixTimestamp))
                    {
                        // Unix timestamp
                        targetTime = DateTime.UnixEpoch.AddSeconds(unixTimestamp);
                    }
                    else
                    {
                        // Try parsing as ISO 8601 string
                        targetTime = DateTime.Parse(value.ToString() ?? "");
                    }

                    var unit = parts[1];
                    var threshold = double.Parse(parts[2]);
                    var timeDiff = DateTime.UtcNow - targetTime;

                    var diffValue = unit switch
                    {
                        "seconds" => timeDiff.TotalSeconds,
                        "minutes" => timeDiff.TotalMinutes,
                        "hours" => timeDiff.TotalHours,
                        "days" => timeDiff.TotalDays,
                        _ => throw new ArgumentException($"Unknown time unit: {unit}")
                    };

                    return diffValue <= threshold ? "pass" : "fail";
                }
                catch
                {
                    return "error";
                }
            }

            // Numeric comparisons
            var val = Convert.ToDouble(value);

            return op switch
            {
                "eq"      => val == double.Parse(parts[1])  ? "pass" : "fail",
                "neq"     => val != double.Parse(parts[1])  ? "pass" : "fail",
                "gt"      => val >  double.Parse(parts[1])  ? "pass" : "fail",
                "gte"     => val >= double.Parse(parts[1])  ? "pass" : "fail",
                "lt"      => val <  double.Parse(parts[1])  ? "pass" : "fail",
                "lte"     => val <= double.Parse(parts[1])  ? "pass" : "fail",
                "between" => val >= double.Parse(parts[1]) &&
                             val <= double.Parse(parts[2])  ? "pass" : "fail",
                _         => "error"
            };
        }
        catch
        {
            return "error";
        }
    }

    /// <summary>
    /// Formats a time difference into a human-readable string (e.g., "2 hours 15 minutes")
    /// </summary>
    public static string FormatTimeDifference(TimeSpan diff)
    {
        var parts = new List<string>();

        if (diff.Days > 0)
            parts.Add($"{diff.Days} day{(diff.Days > 1 ? "s" : "")}");
        if (diff.Hours > 0)
            parts.Add($"{diff.Hours} hour{(diff.Hours > 1 ? "s" : "")}");
        if (diff.Minutes > 0)
            parts.Add($"{diff.Minutes} minute{(diff.Minutes > 1 ? "s" : "")}");
        if (diff.Seconds > 0 && parts.Count == 0)
            parts.Add($"{diff.Seconds} second{(diff.Seconds > 1 ? "s" : "")}");

        return parts.Count > 0 ? string.Join(" ", parts) : "just now";
    }

    /// <summary>
    /// Formats the threshold for display (e.g., "1 hour", "30 minutes")
    /// </summary>
    public static string FormatThreshold(string unit, double threshold)
    {
        var value = threshold == (int)threshold ? $"{(int)threshold}" : $"{threshold}";
        var plural = threshold > 1 ? "s" : "";
        return $"{value} {unit}{plural}";
    }
}
