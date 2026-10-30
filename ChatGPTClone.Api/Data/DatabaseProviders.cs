using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ChatGPTClone.Api.Data;

/// <summary>The two supported databases, and the few places where they differ.</summary>
public static class DatabaseProviders
{
    public const string SqlServer = "SqlServer";
    public const string PostgreSql = "PostgreSQL";

    public static bool IsPostgres(this DbContext db) => db.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <summary>A duplicate key: another request inserted the same unique value first.</summary>
    public static bool IsUniqueViolation(DbUpdateException exception) => exception.InnerException switch
    {
        SqlException sql => sql.Number is 2601 or 2627,
        PostgresException postgres => postgres.SqlState == PostgresErrorCodes.UniqueViolation,
        _ => false,
    };

    /// <summary>
    /// Hosts such as Render hand out "postgres://user:password@host:port/database" URLs; Npgsql wants key=value pairs.
    /// Key=value strings pass through unchanged.
    /// </summary>
    public static string ToNpgsqlConnectionString(string connectionString)
    {
        if (!connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return connectionString;
        }

        var uri = new Uri(connectionString);
        var credentials = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : null,
            // Render's internal URLs are inside its private network; external ones require TLS.
            SslMode = uri.Host.Contains('.') ? SslMode.Require : SslMode.Prefer,
        };
        return builder.ConnectionString;
    }
}
