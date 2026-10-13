using ChatGPTClone.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatGPTClone.Api.Data.Configurations;

public sealed class OtpVerificationConfiguration : IEntityTypeConfiguration<OtpVerification>
{
    public void Configure(EntityTypeBuilder<OtpVerification> builder)
    {
        builder.ToTable("OtpVerifications", t =>
            t.HasCheckConstraint("CK_OtpVerifications_AttemptCount", "[AttemptCount] >= 0"));
        builder.HasKey(o => o.OtpVerificationId);

        // HMAC-SHA256 as hex = 64 characters.
        builder.Property(o => o.OtpHash).HasMaxLength(128).IsUnicode(false).IsRequired();
        builder.Property(o => o.IsUsed).HasDefaultValue(false);
        builder.Property(o => o.AttemptCount).HasDefaultValue(0);
        builder.Property(o => o.ExpiresAt).HasColumnType("datetime2(3)");
        builder.Property(o => o.CreatedAt).HasColumnType("datetime2(3)");

        builder.HasOne(o => o.User)
            .WithMany(u => u.OtpVerifications)
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // "Latest OTP for this user" and "invalidate all active OTPs" lookups.
        builder.HasIndex(o => new { o.UserId, o.IsUsed, o.CreatedAt })
            .HasDatabaseName("IX_OtpVerifications_UserId_IsUsed_CreatedAt");
    }
}
