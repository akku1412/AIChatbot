using ChatGPTClone.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatGPTClone.Api.Data.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.UserId);

        builder.Property(u => u.GoogleId).HasMaxLength(255).IsRequired();
        builder.Property(u => u.FullName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(256).IsRequired();
        builder.Property(u => u.ProfileImageUrl).HasMaxLength(2048);
        builder.Property(u => u.IsEmailVerified).HasDefaultValue(false);
        builder.Property(u => u.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(u => u.UpdatedAt).HasColumnType("datetime2(3)");

        // One account per email and per Google identity.
        builder.HasIndex(u => u.Email).IsUnique().HasDatabaseName("UX_Users_Email");
        builder.HasIndex(u => u.GoogleId).IsUnique().HasDatabaseName("UX_Users_GoogleId");
    }
}
