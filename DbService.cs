using Dapper;
using MySqlConnector;

namespace MediahostHealthMCP;

public sealed class DbService
{
    private readonly string _connectionString;

    public DbService(IConfiguration config)
    {
        // Try to use connection string from appsettings.json first
        var connString = config.GetConnectionString("MySQL");
        
        if (!string.IsNullOrEmpty(connString))
        {
            _connectionString = connString;
            return;
        }

        // Fall back to individual env vars injected by Infisical
        var host     = config["DB_HOST"]     ?? throw new InvalidOperationException("DB_HOST not set");
        var port     = config["DB_PORT"]     ?? "3306";
        var database = config["DB_NAME"]     ?? throw new InvalidOperationException("DB_NAME not set");
        var user     = config["DB_USER"]     ?? throw new InvalidOperationException("DB_USER not set");
        var password = config["DB_PASSWORD"] ?? throw new InvalidOperationException("DB_PASSWORD not set");

        _connectionString =
            $"Server={host};Port={port};Database={database};" +
            $"User ID={user};Password={password};" +
            $"Connect Timeout=10;AllowPublicKeyRetrieval=true;SslMode=Preferred;";
    }

    /// <summary>
    /// Executes a query that must return a single scalar value.
    /// Uses a read-only connection — ensure the DB user has SELECT grants only.
    /// </summary>
    public async Task<object?> QueryScalarAsync(string sql)
    {
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync(sql);
    }
}
