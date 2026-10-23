using ChatGPTClone.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatGPTClone.Api.Data.Configurations;

public sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public const int TitleMaxLength = 200;

    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversations");
        builder.HasKey(c => c.ConversationId);

        builder.Property(c => c.Title).HasMaxLength(TitleMaxLength).IsRequired();
        builder.Property(c => c.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(c => c.UpdatedAt).HasColumnType("datetime2(3)");
        builder.Property(c => c.IsPinned).HasDefaultValue(false);
        builder.Property(c => c.ArchivedAt).HasColumnType("datetime2(3)");

        builder.HasOne(c => c.User)
            .WithMany(u => u.Conversations)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Sidebar query: a user's conversations, most recently active first, paginated.
        builder.HasIndex(c => new { c.UserId, c.UpdatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_Conversations_UserId_UpdatedAt");

        // Sidebar with pin/archive: a user's active (or archived) chats, pinned first, then most recent.
        builder.HasIndex(c => new { c.UserId, c.ArchivedAt, c.IsPinned, c.UpdatedAt })
            .IsDescending(false, false, true, true)
            .HasDatabaseName("IX_Conversations_UserId_Archived_Pinned_UpdatedAt");
    }
}
