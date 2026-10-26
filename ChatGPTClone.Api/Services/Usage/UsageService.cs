using System.Runtime.CompilerServices;
using System.Text;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Data;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Middleware;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChatGPTClone.Api.Services.Usage;

/// <summary>What one action adds to a user's day.</summary>
public sealed record UsageDelta(int Messages = 0, long InputTokens = 0, long OutputTokens = 0, int Uploads = 0, long UploadBytes = 0);

public interface IUsageService
{
    /// <summary>429 "quota_exceeded" when the user already used today's token quota.</summary>
    Task EnsureTokenQuotaAsync(int userId, CancellationToken cancellationToken);

    /// <summary>429 "quota_exceeded" when the user already uploaded today's quota of files.</summary>
    Task EnsureUploadQuotaAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Adds to the user's row for today (created on first use). Safe under concurrent requests.</summary>
    Task RecordAsync(int userId, UsageDelta delta, CancellationToken cancellationToken);
}

public sealed class UsageService(AppDbContext db, IOptions<UsageOptions> options, TimeProvider time) : IUsageService
{
    private readonly UsageOptions _options = options.Value;

    /// <summary>Quotas and the report use UTC days.</summary>
    public static DateTime Today(TimeProvider time) => time.GetUtcNow().UtcDateTime.Date;

    public async Task EnsureTokenQuotaAsync(int userId, CancellationToken cancellationToken)
    {
        if (_options.DailyTokenQuota <= 0)
        {
            return;
        }
        var today = Today(time);
        var used = await db.UsageDaily.AsNoTracking()
            .Where(u => u.UserId == userId && u.Date == today)
            .Select(u => u.InputTokens + u.OutputTokens)
            .FirstOrDefaultAsync(cancellationToken);
        if (used >= _options.DailyTokenQuota)
        {
            throw QuotaExceeded("tokens",
                $"You've used today's limit of {_options.DailyTokenQuota:N0} AI tokens. It resets at midnight UTC.");
        }
    }

    public async Task EnsureUploadQuotaAsync(int userId, CancellationToken cancellationToken)
    {
        if (_options.DailyUploadQuota <= 0)
        {
            return;
        }
        var today = Today(time);
        var used = await db.UsageDaily.AsNoTracking()
            .Where(u => u.UserId == userId && u.Date == today)
            .Select(u => u.UploadCount)
            .FirstOrDefaultAsync(cancellationToken);
        if (used >= _options.DailyUploadQuota)
        {
            throw QuotaExceeded("uploads",
                $"You've uploaded today's limit of {_options.DailyUploadQuota} files. It resets at midnight UTC.");
        }
    }

    public async Task RecordAsync(int userId, UsageDelta delta, CancellationToken cancellationToken)
    {
        var today = Today(time);
        // One atomic statement: HOLDLOCK keeps two first requests of the day from both inserting.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            MERGE [UsageDaily] WITH (HOLDLOCK) AS t
            USING (SELECT {userId} AS UserId, CAST({today} AS date) AS [Date]) AS s
                ON t.UserId = s.UserId AND t.[Date] = s.[Date]
            WHEN MATCHED THEN UPDATE SET
                MessageCount = t.MessageCount + {delta.Messages},
                InputTokens = t.InputTokens + {delta.InputTokens},
                OutputTokens = t.OutputTokens + {delta.OutputTokens},
                UploadCount = t.UploadCount + {delta.Uploads},
                UploadBytes = t.UploadBytes + {delta.UploadBytes}
            WHEN NOT MATCHED THEN
                INSERT (UserId, [Date], MessageCount, InputTokens, OutputTokens, UploadCount, UploadBytes)
                VALUES (s.UserId, s.[Date], {delta.Messages}, {delta.InputTokens}, {delta.OutputTokens}, {delta.Uploads}, {delta.UploadBytes});
            """, cancellationToken);
    }

    private static ApiException QuotaExceeded(string quota, string detail) =>
        new(StatusCodes.Status429TooManyRequests, "Daily limit reached", detail,
            new Dictionary<string, object?> { ["reason"] = "quota_exceeded", ["quota"] = quota });
}

/// <summary>Rough token count (about 4 characters each) when the provider reports none, e.g. a reply stopped part-way.</summary>
public static class TokenEstimate
{
    /// <summary>What a provider typically charges for one image at normal detail.</summary>
    public const int PerImage = 800;

    public static long Of(string text) => (text.Length + 3) / 4;

    public static AiUsage For(IReadOnlyList<AiChatMessage> prompt, string output) =>
        new(prompt.Sum(m => Of(m.Content) + (m.Images?.Count ?? 0) * PerImage), Of(output));
}

/// <summary>
/// Wraps a provider so every call it makes for a user is added to that user's usage: the provider's own token counts,
/// or an estimate when it reports none (a stopped stream still costs what was generated).
/// </summary>
public sealed class MeteredAiChatProvider(IAiChatProvider inner, Func<AiUsage, Task> record) : IAiChatProvider
{
    public string Name => inner.Name;

    public async Task<AiCompletion> CompleteAsync(IReadOnlyList<AiChatMessage> messages, int maxOutputTokens,
        CancellationToken cancellationToken)
    {
        var completion = await inner.CompleteAsync(messages, maxOutputTokens, cancellationToken);
        await record(completion.Usage ?? TokenEstimate.For(messages, completion.Content));
        return completion;
    }

    public async IAsyncEnumerable<AiStreamUpdate> StreamAsync(IReadOnlyList<AiChatMessage> messages, int maxOutputTokens,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        AiUsage? usage = null;
        try
        {
            await foreach (var update in inner.StreamAsync(messages, maxOutputTokens, cancellationToken).WithCancellation(cancellationToken))
            {
                output.Append(update.Text);
                usage = update.Usage ?? usage;
                yield return update;
            }
        }
        finally
        {
            // Runs on completion, failure and Stop alike. A call that failed before any text is not counted.
            if (usage is not null || output.Length > 0)
            {
                await record(usage ?? TokenEstimate.For(messages, output.ToString()));
            }
        }
    }
}
