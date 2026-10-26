using ChatGPTClone.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatGPTClone.Api.Data.Configurations;

public sealed class UserSettingsConfiguration : IEntityTypeConfiguration<UserSettings>
{
    public void Configure(EntityTypeBuilder<UserSettings> builder)
    {
        builder.ToTable("UserSettings");
        builder.HasKey(s => s.UserId); // one row per user

        builder.Property(s => s.AboutMe).HasMaxLength(UserSettings.MaxInstructionLength);
        builder.Property(s => s.ResponseStyle).HasMaxLength(UserSettings.MaxInstructionLength);
        builder.Property(s => s.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(s => s.UpdatedAt).HasColumnType("datetime2(3)");

        builder.HasOne(s => s.User)
            .WithOne()
            .HasForeignKey<UserSettings>(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
