using ChatGPTClone.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatGPTClone.Api.Data.Configurations;

public sealed class UploadedFileConfiguration : IEntityTypeConfiguration<UploadedFile>
{
    public void Configure(EntityTypeBuilder<UploadedFile> builder)
    {
        builder.ToTable("UploadedFiles", t =>
        {
            t.HasCheckConstraint("CK_UploadedFiles_Status", "[ProcessingStatus] IN ('uploaded', 'processing', 'completed', 'failed')");
            t.HasCheckConstraint("CK_UploadedFiles_FileSize", "[FileSize] >= 0");
        });
        builder.HasKey(f => f.FileId);
        builder.Property(f => f.FileId).ValueGeneratedNever();

        builder.Property(f => f.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(f => f.StorageKey).HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(f => f.ContentType).HasMaxLength(150).IsUnicode(false).IsRequired();
        builder.Property(f => f.FileExtension).HasMaxLength(10).IsUnicode(false).IsRequired();
        builder.Property(f => f.ProcessingStatus)
            .HasConversion(s => s.ToString().ToLower(), v => Enum.Parse<FileProcessingStatus>(v, true))
            .HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(f => f.StatusMessage).HasMaxLength(500);
        builder.Property(f => f.StatusReason).HasMaxLength(50).IsUnicode(false);
        builder.Ignore(f => f.IsImage);
        builder.Property(f => f.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(f => f.UpdatedAt).HasColumnType("datetime2(3)");

        builder.HasOne(f => f.User)
            .WithMany()
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(f => f.StorageKey).IsUnique().HasDatabaseName("UX_UploadedFiles_StorageKey");
        builder.HasIndex(f => new { f.UserId, f.CreatedAt }).HasDatabaseName("IX_UploadedFiles_UserId_CreatedAt");
    }
}

public sealed class DocumentChunkConfiguration : IEntityTypeConfiguration<DocumentChunk>
{
    public void Configure(EntityTypeBuilder<DocumentChunk> builder)
    {
        builder.ToTable("DocumentChunks");
        builder.HasKey(c => c.ChunkId);

        builder.Property(c => c.Kind)
            .HasConversion(k => k.ToString().ToLower(), v => Enum.Parse<DocumentChunkKind>(v, true))
            .HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(c => c.Location).HasMaxLength(300).IsRequired();
        builder.Property(c => c.Content).IsRequired(); // nvarchar(max), bounded by MaxExtractedCharacters
        builder.Property(c => c.Source)
            .HasConversion(s => s.ToString().ToLower(), v => Enum.Parse<ChunkSource>(v, true))
            .HasMaxLength(10).IsUnicode(false).IsRequired()
            .HasDefaultValue(ChunkSource.Text);

        builder.HasOne(c => c.File)
            .WithMany(f => f.Chunks)
            .HasForeignKey(c => c.FileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.FileId, c.ChunkIndex }).IsUnique().HasDatabaseName("UX_DocumentChunks_FileId_ChunkIndex");
    }
}

public sealed class ChunkEmbeddingConfiguration : IEntityTypeConfiguration<ChunkEmbedding>
{
    public void Configure(EntityTypeBuilder<ChunkEmbedding> builder)
    {
        builder.ToTable("ChunkEmbeddings", t => t.HasCheckConstraint("CK_ChunkEmbeddings_Dimensions", "[Dimensions] > 0"));
        builder.HasKey(e => e.ChunkId);
        builder.Property(e => e.ChunkId).ValueGeneratedNever();
        builder.Property(e => e.Model).HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(e => e.Vector).IsRequired(); // varbinary(max): SQL Server 2016 has no vector type

        builder.HasOne(e => e.Chunk)
            .WithOne(c => c.Embedding)
            .HasForeignKey<ChunkEmbedding>(e => e.ChunkId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class MessageSourceConfiguration : IEntityTypeConfiguration<MessageSource>
{
    public void Configure(EntityTypeBuilder<MessageSource> builder)
    {
        builder.ToTable("MessageSources");
        builder.HasKey(s => s.MessageSourceId);

        builder.HasOne(s => s.Message)
            .WithMany(m => m.Sources)
            .HasForeignKey(s => s.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        // NO ACTION: second cascade path (Users -> UploadedFiles -> DocumentChunks -> MessageSources).
        // FileService deletes a file's source rows itself before deleting the file.
        builder.HasOne(s => s.Chunk)
            .WithMany()
            .HasForeignKey(s => s.ChunkId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(s => new { s.MessageId, s.ChunkId }).IsUnique().HasDatabaseName("UX_MessageSources_MessageId_ChunkId");
        builder.HasIndex(s => s.ChunkId).HasDatabaseName("IX_MessageSources_ChunkId");
    }
}

public sealed class MessageAttachmentConfiguration : IEntityTypeConfiguration<MessageAttachment>
{
    public void Configure(EntityTypeBuilder<MessageAttachment> builder)
    {
        builder.ToTable("MessageAttachments");
        builder.HasKey(a => a.MessageAttachmentId);

        builder.HasOne(a => a.Message)
            .WithMany(m => m.Attachments)
            .HasForeignKey(a => a.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        // NO ACTION: SQL Server forbids a second cascade path (Users -> UploadedFiles -> MessageAttachments).
        // FileService deletes a file's attachment rows itself before deleting the file.
        builder.HasOne(a => a.File)
            .WithMany()
            .HasForeignKey(a => a.FileId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(a => new { a.MessageId, a.FileId }).IsUnique().HasDatabaseName("UX_MessageAttachments_MessageId_FileId");
        builder.HasIndex(a => a.FileId).HasDatabaseName("IX_MessageAttachments_FileId");
    }
}
