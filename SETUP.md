# Mediahost Health Check MCP — Setup Guide (C# / Minimal API)

## Project structure

```
mediahost-health-mcp-csharp/
├── MediahostHealth.csproj   # SDK Web project, .NET 9
├── Program.cs               # Minimal API entry point
├── HealthCheckTools.cs      # MCP tools (ListChecks, RunCheck, RunAllChecks)
├── DbService.cs             # Dapper + MySqlConnector, reads env vars only
├── Models.cs                # HealthCheck, CheckResult, YAML loader, evaluator
├── appsettings.json         # Kestrel port only — no secrets
├── queries.yaml             # Your 30 health check definitions
├── Containerfile            # Podman multi-stage build
└── SETUP.md                 # This file
```

---

## 1. Create a read-only MySQL user

```sql
CREATE USER 'healthcheck'@'%' IDENTIFIED BY 'strong-password-here';
GRANT SELECT ON your_database.* TO 'healthcheck'@'%';
FLUSH PRIVILEGES;
```

---

## 2. Store credentials in Infisical

Add these to your Infisical project (environment: `prod`):

| Key            | Value                    |
|----------------|--------------------------|
| `DB_HOST`      | your DB host or IP       |
| `DB_PORT`      | `3306`                   |
| `DB_NAME`      | your database name       |
| `DB_USER`      | `healthcheck`            |
| `DB_PASSWORD`  | the password from step 1 |
| `QUERIES_FILE` | `/app/queries.yaml`      |

---

## 3. Build and run with Podman

```bash
# Build
podman build -t mediahost-health-mcp .

# Run — Infisical injects env vars, queries.yaml mounted read-only
infisical run --env=prod -- podman run \
  --name mediahost-health \
  --restart=always \
  -p 8000:8000 \
  -v $(pwd)/queries.yaml:/app/queries.yaml:ro \
  mediahost-health-mcp
```

---

## 4. Reverse proxy (Nginx)

```nginx
location /mcp/health/ {
    proxy_pass         http://127.0.0.1:8000/;
    proxy_http_version 1.1;

    # Required for Streamable HTTP / SSE transport
    proxy_set_header   Connection '';
    proxy_buffering    off;
    chunked_transfer_encoding on;
}
```

MCP endpoint will be at: `https://yourdomain.com/mcp/health/mcp`
Health ping at:           `https://yourdomain.com/mcp/health/health`

---

## 5. Verify

```bash
curl https://yourdomain.com/mcp/health/health
# {"status":"ok"}
```

---

## 6. Add as a connector in Claude Code

`claude.ai/code/connectors` → Add custom connector

```
Name:  Mediahost Health
URL:   https://yourdomain.com/mcp/health/mcp
```

---

## 7. Create the routine

At `claude.ai/code/routines` → New routine, or in a Claude Code session:

```
/schedule daily health report at 5am
```

**Routine prompt:**

```
You are running the Mediahost morning health check.

1. Call list_checks to get all defined checks.
2. Call run_all_checks to execute every query.
3. Format the report as follows:

---
🏥 MEDIAHOST HEALTH REPORT
{date} — {time} UTC

✅ PASSING ({n})
  • {description}

❌ FAILING ({n})
  • {description}
    → {fail_message}

⚠️  ERRORS ({n})
  • {name}: {error}

Overall status: HEALTHY / DEGRADED / CRITICAL
  HEALTHY  = all pass
  DEGRADED = 1–3 failures
  CRITICAL = 4+ failures or any error
---

Post to the connected Slack/Telegram channel,
or save as health-report-{date}.md in the repo
if no messaging connector is attached.
```

---

## 8. Adding more checks

Edit `queries.yaml` — no restart or rebuild needed.

```yaml
- name: your_check_name
  description: "Human readable label"
  query: "SELECT COUNT(*) FROM your_table WHERE condition"
  pass_condition: "eq:0"
  fail_message: "{value} records in unexpected state"
```

Pass condition operators:

| Operator        | Meaning          |
|-----------------|------------------|
| `eq:0`          | value == 0       |
| `neq:0`         | value != 0       |
| `gt:0`          | value > 0        |
| `gte:1`         | value >= 1       |
| `lt:100`        | value < 100      |
| `lte:99`        | value <= 99      |
| `between:1:10`  | 1 ≤ value ≤ 10   |
