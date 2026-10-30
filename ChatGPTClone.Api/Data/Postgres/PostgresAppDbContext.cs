using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ChatGPTClone.Api.Data.Postgres;

/// <summary>
/// The same model on PostgreSQL (e.g. Render's free database), with its own migrations in Data/Postgres/Migrations.
/// The entity configurations are written for SQL Server; the few SQL Server-only details are translated here.
/// </summary>
public sealed class PostgresAppDbContext(DbContextOptions<PostgresAppDbContext> options) : AppDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnType(property.GetColumnType() switch
                {
                    // UTC timestamps (the converter always writes DateTimeKind.Utc), millisecond precision like datetime2(3).
                    "datetime2(3)" => "timestamp(3) with time zone",
                    "binary(32)" => "bytea",
                    var other => other,
                });
            }

            // SQL Server quotes identifiers as [Name], PostgreSQL as "Name" (index filters and check constraints).
            foreach (var index in entity.GetIndexes())
            {
                if (index.GetFilter() is { } filter)
                {
                    index.SetFilter(QuoteIdentifiers(filter));
                }
            }
            foreach (var check in entity.GetCheckConstraints().ToList())
            {
                entity.RemoveCheckConstraint(check.ModelName);
                entity.AddCheckConstraint(check.ModelName, QuoteIdentifiers(check.Sql));
            }
        }
    }

    private static string QuoteIdentifiers(string sql) => sql.Replace('[', '"').Replace(']', '"');
}

/// <summary>Lets "dotnet ef migrations add … --context PostgresAppDbContext" build the model without a database.</summary>
public sealed class PostgresAppDbContextFactory : IDesignTimeDbContextFactory<PostgresAppDbContext>
{
    public PostgresAppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PostgresAppDbContext>()
            .UseNpgsql("Host=localhost;Database=design_time_only")
            .Options);
}
