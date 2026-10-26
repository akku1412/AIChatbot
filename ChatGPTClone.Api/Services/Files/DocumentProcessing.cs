using System.Threading.Channels;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Data;
using ChatGPTClone.Api.Models;
using ChatGPTClone.Api.Services.Embeddings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChatGPTClone.Api.Services.Files;

public enum DocumentJobKind
{
    /// <summary>Extract text (and OCR), chunk, then index.</summary>
    Process,

    /// <summary>Only (re)compute missing embeddings of an already processed file.</summary>
    Index,
}

public sealed record DocumentJob(Guid FileId, DocumentJobKind Kind);

/// <summary>In-memory work queue for uploaded files. Unfinished work is re-queued from the database at startup.</summary>
public interface IDocumentProcessingQueue
{
    void Enqueue(DocumentJob job);
    IAsyncEnumerable<DocumentJob> ReadAllAsync(CancellationToken cancellationToken);
}

public sealed class DocumentProcessingQueue : IDocumentProcessingQueue
{
    private readonly Channel<DocumentJob> _channel = Channel.CreateUnbounded<DocumentJob>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(DocumentJob job) => _channel.Writer.TryWrite(job);

    public IAsyncEnumerable<DocumentJob> ReadAllAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>
/// Runs document jobs one at a time, outside requests, so uploads return immediately and slow work (OCR, embeddings)
/// never ties up a request. At startup it resumes files left "processing" by a restart and indexes files that have
/// no embeddings for the current model yet (e.g. files uploaded before semantic search existed).
/// </summary>
public sealed class DocumentProcessingWorker(
    IDocumentProcessingQueue queue,
    IServiceScopeFactory scopes,
    ILogger<DocumentProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // let the host finish starting
        try
        {
            await ResumeAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not resume unfinished document work");
        }

        await foreach (var job in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IDocumentProcessor>();
                if (job.Kind == DocumentJobKind.Process)
                {
                    await processor.ProcessAsync(job.FileId, stoppingToken);
                }
                else
                {
                    await processor.IndexAsync(job.FileId, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Document job {Kind} for file {FileId} failed", job.Kind, job.FileId);
            }
        }
    }

    private async Task ResumeAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var embeddings = scope.ServiceProvider.GetRequiredService<IEmbeddingProvider>();

        var unfinished = await db.UploadedFiles.AsNoTracking()
            .Where(f => f.ProcessingStatus == FileProcessingStatus.Processing || f.ProcessingStatus == FileProcessingStatus.Uploaded)
            .Select(f => f.FileId)
            .ToListAsync(cancellationToken);
        unfinished.ForEach(id => queue.Enqueue(new DocumentJob(id, DocumentJobKind.Process)));

        if (embeddings.IsAvailable)
        {
            var model = embeddings.Model;
            var unindexed = await db.DocumentChunks.AsNoTracking()
                .Where(c => c.Kind == DocumentChunkKind.Text && c.File.ProcessingStatus == FileProcessingStatus.Completed
                            && (c.Embedding == null || c.Embedding.Model != model))
                .Select(c => c.FileId)
                .Distinct()
                .ToListAsync(cancellationToken);
            unindexed.ForEach(id => queue.Enqueue(new DocumentJob(id, DocumentJobKind.Index)));
            if (unindexed.Count > 0)
            {
                logger.LogInformation("Indexing {Count} existing file(s) for semantic search", unindexed.Count);
            }
        }
        if (unfinished.Count > 0)
        {
            logger.LogInformation("Resuming processing of {Count} file(s)", unfinished.Count);
        }
    }
}

public interface IDocumentProcessor
{
    /// <summary>Extracts, chunks and indexes one uploaded file, then marks it completed (or failed with a reason).</summary>
    Task ProcessAsync(Guid fileId, CancellationToken cancellationToken);

    /// <summary>Embeds the file's text chunks that have no vector for the current model.</summary>
    Task IndexAsync(Guid fileId, CancellationToken cancellationToken);
}

public sealed class DocumentProcessor(
    AppDbContext db,
    IFileStorage storage,
    IEnumerable<IDocumentExtractor> extractors,
    IEmbeddingProvider embeddings,
    IOptions<FileUploadOptions> options,
    IOptions<EmbeddingOptions> embeddingOptions,
    ILogger<DocumentProcessor> logger) : IDocumentProcessor
{
    private readonly FileUploadOptions _options = options.Value;

    public async Task ProcessAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var file = await db.UploadedFiles.FirstOrDefaultAsync(f => f.FileId == fileId, cancellationToken);
        if (file is null || file.ProcessingStatus is FileProcessingStatus.Completed or FileProcessingStatus.Failed)
        {
            return; // deleted meanwhile, or already done
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.ProcessingTimeoutSeconds));
        try
        {
            var result = await ExtractWithProgressAsync(file, timeout.Token);

            // Re-processing after a restart: start from a clean slate.
            await db.DocumentChunks.Where(c => c.FileId == fileId).ExecuteDeleteAsync(cancellationToken);
            var chunks = DocumentChunker.Chunk(fileId, result.Sections, _options.ChunkCharacters);
            db.DocumentChunks.AddRange(chunks);
            file.PageCount = result.PageCount;
            file.ExtractedCharacters = result.Sections.Sum(s => s.Text.Length);
            file.StatusMessage = "Indexing for search";
            file.ProgressCurrent = null;
            file.ProgressTotal = null;
            await db.SaveChangesAsync(cancellationToken);

            await IndexAsync(fileId, timeout.Token);

            // A direct update: indexing clears the change tracker, so "file" is no longer tracked here.
            await db.UploadedFiles.Where(f => f.FileId == fileId).ExecuteUpdateAsync(s => s
                .SetProperty(f => f.ProcessingStatus, FileProcessingStatus.Completed)
                .SetProperty(f => f.StatusMessage, result.Warning)
                .SetProperty(f => f.StatusReason, (string?)null)
                .SetProperty(f => f.ProgressCurrent, (int?)null)
                .SetProperty(f => f.ProgressTotal, (int?)null)
                .SetProperty(f => f.UpdatedAt, DateTime.UtcNow), cancellationToken);
            logger.LogInformation("File {FileId} ({Extension}) processed into {Chunks} chunks ({Ocr} from OCR)",
                fileId, file.FileExtension, chunks.Count, chunks.Count(c => c.Source == ChunkSource.Ocr));
        }
        catch (DocumentExtractionException ex)
        {
            await FailAsync(file, ex.Message, ex.Reason);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await FailAsync(file, "This file took too long to process. Try a smaller file or split it into parts.", "timeout");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Processing file {FileId} failed", fileId);
            await FailAsync(file, "We couldn't process this file. Please try again.", "processing_failed");
        }
    }

    public async Task IndexAsync(Guid fileId, CancellationToken cancellationToken)
    {
        if (!embeddings.IsAvailable)
        {
            return;
        }
        var model = embeddings.Model;
        var pending = await db.DocumentChunks.AsNoTracking()
            .Where(c => c.FileId == fileId && c.Kind == DocumentChunkKind.Text && (c.Embedding == null || c.Embedding.Model != model))
            .OrderBy(c => c.ChunkIndex)
            .Select(c => new { c.ChunkId, c.Content, c.Location })
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
        {
            return;
        }

        try
        {
            var batchSize = embeddingOptions.Value.BatchSize;
            for (var start = 0; start < pending.Count; start += batchSize)
            {
                var batch = pending.Skip(start).Take(batchSize).ToList();
                await ReportProgressAsync(fileId, start, pending.Count, cancellationToken);
                // The location helps short chunks ("Slide 4", "Sheet 'Q1'") match questions that name them.
                var vectors = await embeddings.EmbedAsync(batch.Select(c => $"{c.Location}\n{c.Content}").ToList(),
                    EmbeddingKind.Passage, cancellationToken);
                var ids = batch.Select(c => c.ChunkId).ToList();
                await db.ChunkEmbeddings.Where(e => ids.Contains(e.ChunkId)).ExecuteDeleteAsync(cancellationToken);
                db.ChunkEmbeddings.AddRange(batch.Select((c, i) => new ChunkEmbedding
                {
                    ChunkId = c.ChunkId,
                    Model = model,
                    Dimensions = vectors[i].Length,
                    Vector = Vectors.ToBytes(vectors[i]),
                }));
                await db.SaveChangesAsync(cancellationToken);
                db.ChangeTracker.Clear();
            }
            await ReportProgressAsync(fileId, null, null, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Semantic search is an improvement, not a requirement: keyword retrieval still works.
            logger.LogWarning(ex, "Indexing file {FileId} for semantic search failed; keyword search still works", fileId);
        }
    }

    private async Task<ExtractionResult> ExtractWithProgressAsync(UploadedFile file, CancellationToken cancellationToken)
    {
        var extractor = extractors.FirstOrDefault(e => e.CanHandle(file.FileExtension))
            ?? throw new DocumentExtractionException("This file type can't be read.", "unsupported");

        // The extractor reports (done, total) from its thread; progress is saved about once a second.
        var gate = new object();
        var progress = (Done: 0, Total: 0);
        var limits = new ExtractionLimits(_options.MaxExtractedCharacters, _options.MaxRowsPerSheet, (done, total) =>
        {
            lock (gate)
            {
                progress = (done, total);
            }
        });

        var work = Task.Run(() =>
        {
            using var stream = storage.OpenRead(file.StorageKey);
            return extractor.Extract(stream, file.FileExtension, limits);
        }, cancellationToken);

        (int, int) saved = (0, 0);
        while (!work.IsCompleted)
        {
            await Task.WhenAny(work, Task.Delay(800, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            (int Done, int Total) now;
            lock (gate)
            {
                now = progress;
            }
            if (now.Total > 0 && now != saved)
            {
                saved = now;
                await db.UploadedFiles.Where(f => f.FileId == file.FileId).ExecuteUpdateAsync(s => s
                    .SetProperty(f => f.ProgressCurrent, now.Done)
                    .SetProperty(f => f.ProgressTotal, now.Total)
                    .SetProperty(f => f.StatusMessage, "Reading scanned pages"), cancellationToken);
            }
        }
        return await work;
    }

    private Task ReportProgressAsync(Guid fileId, int? done, int? total, CancellationToken cancellationToken) =>
        db.UploadedFiles.Where(f => f.FileId == fileId).ExecuteUpdateAsync(s => s
            .SetProperty(f => f.ProgressCurrent, done)
            .SetProperty(f => f.ProgressTotal, total), cancellationToken);

    /// <summary>The file stays (so the user sees why) but its content is removed: nothing half-processed remains.</summary>
    private async Task FailAsync(UploadedFile file, string message, string reason)
    {
        logger.LogInformation("File {FileId} could not be processed: {Reason}", file.FileId, reason);
        db.ChangeTracker.Clear();
        await db.DocumentChunks.Where(c => c.FileId == file.FileId).ExecuteDeleteAsync();
        await db.UploadedFiles.Where(f => f.FileId == file.FileId).ExecuteUpdateAsync(s => s
            .SetProperty(f => f.ProcessingStatus, FileProcessingStatus.Failed)
            .SetProperty(f => f.StatusMessage, message)
            .SetProperty(f => f.StatusReason, reason)
            .SetProperty(f => f.ProgressCurrent, (int?)null)
            .SetProperty(f => f.ProgressTotal, (int?)null)
            .SetProperty(f => f.UpdatedAt, DateTime.UtcNow));
        try
        {
            await storage.DeleteAsync(file.StorageKey);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete the stored copy of failed file {FileId}", file.FileId);
        }
    }
}
