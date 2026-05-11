using MediahostHealth;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

// ---------------------------------------------------------------------------
// Services
// ---------------------------------------------------------------------------

// DbService reads DB_HOST / DB_NAME / DB_USER / DB_PASSWORD from environment.
// These are injected by Infisical at container startup — never in appsettings.
builder.Services.AddSingleton<DbService>();

// HealthCheckTools provides the health check operations
builder.Services.AddScoped<HealthCheckTools>();

// EmailService sends health check reports via Office 365 SMTP
// Configuration: appsettings.json -> Email section
builder.Services.AddSingleton<EmailService>();

// MCP server — scans this assembly for [McpServerToolType] classes
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

// ---------------------------------------------------------------------------
// App
// ---------------------------------------------------------------------------

var app = builder.Build();

// Health ping — lets your reverse proxy verify the container is alive
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Run health checks and email report — GET endpoint for easy scheduling
app.MapGet("/run-and-email", async ([FromServices] HealthCheckTools tools) =>
{
    var result = await tools.RunChecksAndEmailReport();
    return Results.Ok(result);
});

// Run checks only — returns raw JSON so the agent can inspect and store the data
app.MapGet("/api/run-checks", async ([FromServices] HealthCheckTools tools) =>
{
    var result = await tools.RunAllChecks();
    return Results.Content(result, "application/json");
});

// Send email only — accepts report JSON from the agent after it has processed the data
app.MapPost("/api/send-email", async (HttpRequest request, [FromServices] HealthCheckTools tools) =>
{
    using var reader = new StreamReader(request.Body);
    var reportJson = await reader.ReadToEndAsync();
    var result = await tools.SendReportEmail(reportJson);
    return Results.Content(result, "application/json");
});

// MCP endpoint — Claude Code Routines connects here
app.MapMcp("/mcp");

app.Run();
