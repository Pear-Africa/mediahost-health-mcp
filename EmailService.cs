using System.Text;
using MailKit.Net.Smtp;
using MimeKit;

namespace MediahostHealthMCP;

public sealed class EmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Sends a health check report email via Office 365 SMTP
    /// </summary>
    public async Task SendReportAsync(ReportSummary report)
    {
        try
        {
            var recipientEmail = _config["Email:Recipient"] ?? throw new InvalidOperationException("Email:Recipient not configured in appsettings.json");
            var senderEmail = _config["Email:Sender"] ?? throw new InvalidOperationException("Email:Sender not configured in appsettings.json");
            var appPassword = _config["Email:AppPassword"] ?? throw new InvalidOperationException("Email:AppPassword not configured in appsettings.json");

            // Build HTML email body
            var htmlBody = BuildEmailBody(report);

            // Create message
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Health Check Bot", senderEmail));
            message.To.Add(new MailboxAddress("", recipientEmail));
            message.Subject = $"Health Check Report - {report.RanAt} - {report.Passing}/{report.Total} Passing";

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
            message.Body = bodyBuilder.ToMessageBody();

            // Send via Office 365 SMTP
            using (var client = new SmtpClient())
            {
                await client.ConnectAsync("smtp.office365.com", 587, MailKit.Security.SecureSocketOptions.StartTls);
                await client.AuthenticateAsync(senderEmail, appPassword);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
            }

            _logger.LogInformation("Health check report sent to {Recipient}", recipientEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send health check report email");
            throw;
        }
    }

    private string BuildEmailBody(ReportSummary report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html>");
        sb.AppendLine("<head><style>");
        sb.AppendLine("body { font-family: Arial, sans-serif; }");
        sb.AppendLine(".summary { margin: 20px 0; }");
        sb.AppendLine(".pass { color: green; font-weight: bold; }");
        sb.AppendLine(".fail { color: red; font-weight: bold; }");
        sb.AppendLine("table { border-collapse: collapse; width: 100%; margin-top: 10px; }");
        sb.AppendLine("th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }");
        sb.AppendLine("th { background-color: #f2f2f2; }");
        sb.AppendLine(".pass-row { background-color: #f0fff0; }");
        sb.AppendLine(".fail-row { background-color: #fff0f0; }");
        sb.AppendLine(".error-row { background-color: #fffaf0; }");
        sb.AppendLine("</style></head>");
        sb.AppendLine("<body>");

        sb.AppendLine("<h2>Health Check Report</h2>");
        sb.AppendLine($"<p>Ran at: <strong>{report.RanAt}</strong></p>");

        sb.AppendLine("<div class='summary'>");
        sb.AppendLine($"<p>Total: <strong>{report.Total}</strong></p>");
        sb.AppendLine($"<p class='pass'>Passing: {report.Passing}</p>");
        sb.AppendLine($"<p class='fail'>Failing: {report.Failing}</p>");
        sb.AppendLine($"<p>Errors: {report.Errors}</p>");
        sb.AppendLine("</div>");

        sb.AppendLine("<table>");
        sb.AppendLine("<tr>");
        sb.AppendLine("<th>Check Name</th>");
        sb.AppendLine("<th>Status</th>");
        sb.AppendLine("<th>Value</th>");
        sb.AppendLine("<th>Message</th>");
        sb.AppendLine("</tr>");

        foreach (var check in report.Checks)
        {
            var rowClass = check.Status switch
            {
                "pass" => "pass-row",
                "fail" => "fail-row",
                "error" => "error-row",
                _ => ""
            };

            sb.AppendLine($"<tr class='{rowClass}'>");
            sb.AppendLine($"<td>{check.Description}</td>");
            sb.AppendLine($"<td><strong>{check.Status.ToUpper()}</strong></td>");
            sb.AppendLine($"<td>{check.Value ?? "N/A"}</td>");
            sb.AppendLine($"<td>{check.FailMessage ?? check.Error ?? "-"}</td>");
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</table>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }
}
