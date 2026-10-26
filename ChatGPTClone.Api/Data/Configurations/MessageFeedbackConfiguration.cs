using ChatGPTClone.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatGPTClone.Api.Data.Configurations;

public sealed class MessageFeedbackConfiguration : IEntityTypeConfiguration<MessageFeedback>
{
    public void Configure(EntityTypeBuilder<MessageFeedback> builder)
    {
        builder.ToTable("MessageFeedback", t => t.HasCheckConstraint("CK_MessageFeedback_Rating", "[Rating] IN (-1, 1)"));
        builder.HasKey(f => f.MessageFeedbackId);

        builder.Property(f => f.Comment).HasMaxLength(MessageFeedback.CommentMaxLength);
        builder.Property(f => f.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(f => f.UpdatedAt).HasColumnType("datetime2(3)");

        // Deleted with its reply (Users -> Conversations -> Messages -> MessageFeedback is the only cascade path).
        builder.HasOne(f => f.Message)
            .WithOne(m => m.Feedback)
            .HasForeignKey<MessageFeedback>(f => f.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(f => f.MessageId).IsUnique().HasDatabaseName("UX_MessageFeedback_MessageId");
        // Admin panel: recent feedback, and per-user reports.
        builder.HasIndex(f => new { f.UserId, f.CreatedAt }).HasDatabaseName("IX_MessageFeedback_UserId_CreatedAt");
        builder.HasIndex(f => f.CreatedAt).HasDatabaseName("IX_MessageFeedback_CreatedAt");
    }
}
