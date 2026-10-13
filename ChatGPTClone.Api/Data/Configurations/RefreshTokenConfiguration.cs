using ChatGPTClone.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatGPTClone.Api.Data.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(r => r.RefreshTokenId);

        // SHA-256 as hex = 64 characters.
        builder.Property(r => r.TokenHash).HasMaxLength(128).IsUnicode(false).IsRequired();
        builder.Property(r => r.ExpiresAt).HasColumnType("datetime2(3)");
        builder.Property(r => r.RevokedAt).HasColumnType("datetime2(3)");
        builder.Property(r => r.CreatedAt).HasColumnType("datetime2(3)");

        builder.HasOne(r => r.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.TokenHash).IsUnique().HasDatabaseName("UX_RefreshTokens_TokenHash");
        builder.HasIndex(r => r.UserId).HasDatabaseName("IX_RefreshTokens_UserId");
    }
}
