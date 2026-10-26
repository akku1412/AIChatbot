using ChatGPTClone.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatGPTClone.Api.Data.Configurations;

public sealed class SharedLinkConfiguration : IEntityTypeConfiguration<SharedLink>
{
    public void Configure(EntityTypeBuilder<SharedLink> builder)
    {
        builder.ToTable("SharedLinks");
        builder.HasKey(s => s.SharedLinkId);

        builder.Property(s => s.TokenHash).HasColumnType("binary(32)").IsRequired();
        builder.Property(s => s.Title).HasMaxLength(ConversationConfiguration.TitleMaxLength).IsRequired();
        builder.Property(s => s.SnapshotJson).IsRequired(); // nvarchar(max)
        builder.Property(s => s.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(s => s.RevokedAt).HasColumnType("datetime2(3)");

        // Deleting the conversation deletes its links (the only cascade path: Users -> Conversations -> SharedLinks).
        builder.HasOne(s => s.Conversation)
            .WithMany()
            .HasForeignKey(s => s.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        // The public lookup.
        builder.HasIndex(s => s.TokenHash).IsUnique().HasDatabaseName("UX_SharedLinks_TokenHash");
        builder.HasIndex(s => new { s.ConversationId, s.RevokedAt }).HasDatabaseName("IX_SharedLinks_ConversationId_RevokedAt");
    }
}
