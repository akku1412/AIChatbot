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

        // Loading a conversation's messages in order.
        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt })
            .HasDatabaseName("IX_Messages_ConversationId_CreatedAt");
    }
}
