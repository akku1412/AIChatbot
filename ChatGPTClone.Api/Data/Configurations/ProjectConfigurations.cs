using ChatGPTClone.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatGPTClone.Api.Data.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects");
        builder.HasKey(p => p.ProjectId);

        builder.Property(p => p.Name).HasMaxLength(Project.NameMaxLength).IsRequired();
        builder.Property(p => p.Instructions).HasMaxLength(Project.InstructionsMaxLength);
        builder.Property(p => p.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(p => p.UpdatedAt).HasColumnType("datetime2(3)");

        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Sidebar: a user's projects by name.
        builder.HasIndex(p => new { p.UserId, p.Name }).HasDatabaseName("IX_Projects_UserId_Name");
    }
}

public sealed class ProjectFileConfiguration : IEntityTypeConfiguration<ProjectFile>
{
    public void Configure(EntityTypeBuilder<ProjectFile> builder)
    {
        builder.ToTable("ProjectFiles");
        builder.HasKey(pf => new { pf.ProjectId, pf.FileId });
        builder.Property(pf => pf.CreatedAt).HasColumnType("datetime2(3)");

        builder.HasOne(pf => pf.Project)
            .WithMany(p => p.Files)
            .HasForeignKey(pf => pf.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // NO ACTION: SQL Server forbids a second cascade path (Users -> UploadedFiles -> ProjectFiles).
        // FileService removes a file's project links itself before deleting the file.
        builder.HasOne(pf => pf.File)
            .WithMany()
            .HasForeignKey(pf => pf.FileId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(pf => pf.FileId).HasDatabaseName("IX_ProjectFiles_FileId");
    }
}
