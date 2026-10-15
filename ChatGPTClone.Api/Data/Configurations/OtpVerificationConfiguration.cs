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

        builder.Property(o => o.Email).HasMaxLength(256).IsRequired();
        // base64(salt) + "." + HMAC-SHA256 hex = 89 characters.
        builder.Property(o => o.OtpHash).HasMaxLength(128).IsUnicode(false).IsRequired();
        builder.Property(o => o.IsUsed).HasDefaultValue(false);
        builder.Property(o => o.AttemptCount).HasDefaultValue(0);
        builder.Property(o => o.ExpiresAt).HasColumnType("datetime2(3)");
        builder.Property(o => o.CreatedAt).HasColumnType("datetime2(3)");

        // "Latest active code for this email", "invalidate older codes" and the hourly cap.
        builder.HasIndex(o => new { o.Email, o.IsUsed, o.CreatedAt })
            .HasDatabaseName("IX_OtpVerifications_Email_IsUsed_CreatedAt");
    }
}
