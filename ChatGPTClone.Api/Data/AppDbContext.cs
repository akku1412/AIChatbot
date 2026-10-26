using ChatGPTClone.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ChatGPTClone.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<OtpVerification> OtpVerifications => Set<OtpVerification>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UploadedFile> UploadedFiles => Set<UploadedFile>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<MessageAttachment> MessageAttachments => Set<MessageAttachment>();
    public DbSet<ChunkEmbedding> ChunkEmbeddings => Set<ChunkEmbedding>();
    public DbSet<MessageSource> MessageSources => Set<MessageSource>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Picks up every IEntityTypeConfiguration<T> in Data/Configurations.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // All timestamps are stored as UTC in datetime2. SQL Server does not keep DateTimeKind,
        // so mark values read back as UTC; JSON then serializes them with a trailing "Z".
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private void StampTimestamps()
    {
        var utcNow = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<IHasCreatedAt>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = utcNow;
            }
            else if (entry.State == EntityState.Modified)
            {
                // CreatedAt is write-once.
                entry.Property(e => e.CreatedAt).IsModified = false;
            }

            // UpdatedAt means "last activity" (it orders the sidebar), so organising a chat (pin, archive) doesn't count.
            var isActivity = entry.State == EntityState.Added ||
                (entry.State == EntityState.Modified &&
                 entry.Properties.Any(p => p.IsModified && !NotActivity.Contains(p.Metadata.Name)));
            if (entry.Entity is IAuditable auditable && isActivity)
            {
                auditable.UpdatedAt = utcNow;
            }
        }
    }

    private static readonly HashSet<string> NotActivity = [nameof(Conversation.IsPinned), nameof(Conversation.ArchivedAt)];

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v.Value : v.Value.ToUniversalTime()) : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
}
