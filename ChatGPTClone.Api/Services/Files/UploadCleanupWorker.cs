using ChatGPTClone.Api.Configuration;
using Microsoft.Extensions.Options;

namespace ChatGPTClone.Api.Services.Files;

/// <summary>
/// Every FileUploads:CleanupIntervalMinutes (default hourly) deletes uploads older than AbandonedUploadRetentionHours
/// (default 24) that were never sent with a message or shared with a project, with their chunks and stored bytes.
/// Files in use are never touched.
/// </summary>
public sealed class UploadCleanupWorker(
    IServiceScopeFactory scopes,
    IOptions<FileUploadOptions> options,
    TimeProvider clock,
    ILogger<UploadCleanupWorker> logger) : BackgroundService
{
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (settings.AbandonedUploadRetentionHours == 0)
        {
            logger.LogInformation("Abandoned-upload cleanup is off (FileUploads:AbandonedUploadRetentionHours = 0)");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(settings.CleanupIntervalMinutes), clock);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Abandoned-upload cleanup failed; it will run again later");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One cleanup pass (in batches, so a large backlog doesn't hold one huge transaction). Returns files deleted.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime.AddHours(-options.Value.AbandonedUploadRetentionHours);
        var total = 0;
        int deleted;
        do
        {
            using var scope = scopes.CreateScope();
            var files = scope.ServiceProvider.GetRequiredService<IFileService>();
            deleted = await files.DeleteAbandonedAsync(cutoff, BatchSize, cancellationToken);
            total += deleted;
        }
        while (deleted == BatchSize && !cancellationToken.IsCancellationRequested);

        if (total > 0)
        {
            logger.LogInformation("Deleted {Count} abandoned uploads (uploaded before {Cutoff:u}, never used)", total, cutoff);
        }
        return total;
    }
}
