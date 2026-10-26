using ChatGPTClone.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatGPTClone.Api.Data.Configurations;

public sealed class UsageDailyConfiguration : IEntityTypeConfiguration<UsageDaily>
{
    public void Configure(EntityTypeBuilder<UsageDaily> builder)
    {
        builder.ToTable("UsageDaily");
        // One row per user per day; UsageService upserts it with MERGE.
        builder.HasKey(u => new { u.UserId, u.Date });
        builder.Property(u => u.Date).HasColumnType("date");

        // The admin report reads whole days across all users.
        builder.HasIndex(u => u.Date).HasDatabaseName("IX_UsageDaily_Date");

        builder.HasOne(u => u.User)
            .WithMany()
            .HasForeignKey(u => u.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
