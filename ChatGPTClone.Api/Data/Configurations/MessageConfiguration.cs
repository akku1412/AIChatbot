using ChatGPTClone.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatGPTClone.Api.Data.Configurations;

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Messages", t =>
        {
            t.HasCheckConstraint("CK_Messages_Role", "[Role] IN ('user', 'assistant', 'system')");
            t.HasCheckConstraint("CK_Messages_TokenUsage", "[TokenUsage] IS NULL OR [TokenUsage] >= 0");
        });
        builder.HasKey(m => m.MessageId);

        builder.Property(m => m.Role)
            .HasConversion(
                role => role.ToString().ToLower(),
                value => Enum.Parse<MessageRole>(value, true))
            .HasMaxLength(20)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(m => m.Content).IsRequired(); // nvarchar(max)
        builder.Property(m => m.CreatedAt).HasColumnType("datetime2(3)");

        builder.HasOne(m => m.Conversation)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(m => m.VersionIndex).HasDefaultValue(1);
        builder.Property(m => m.IsActive).HasDefaultValue(true);
        builder.Property(m => m.IsTruncated).HasDefaultValue(false);

        // Self-reference for versions/branches. NO ACTION: SQL Server forbids a second cascade path
        // (deleting a conversation already cascades to every message).
        builder.HasOne(m => m.ParentMessage)
            .WithMany()
            .HasForeignKey(m => m.ParentMessageId)
            .OnDelete(DeleteBehavior.NoAction);

        // Loading a conversation's messages in order.
        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt })
            .HasDatabaseName("IX_Messages_ConversationId_CreatedAt");

        // Finding a message's versions (siblings) and children.
        builder.HasIndex(m => new { m.ConversationId, m.ParentMessageId })
            .HasDatabaseName("IX_Messages_ConversationId_ParentMessageId");
    }
}
