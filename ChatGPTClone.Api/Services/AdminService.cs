using ChatGPTClone.Api.Authentication;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Data;
using ChatGPTClone.Api.DTOs.Admin;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Middleware;
using ChatGPTClone.Api.Models;
using ChatGPTClone.Api.Services.Usage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChatGPTClone.Api.Services;

/// <summary>The admin panel. Only reachable through AdminController ([Authorize(Roles = "Admin")]).</summary>
public interface IAdminService
{
    Task<AdminUserPageDto> GetUsersAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Blocks or unblocks an account. Blocking also ends its sessions (refresh tokens revoked).</summary>
    Task<AdminUserDto> SetBlockedAsync(int adminUserId, int userId, bool blocked, CancellationToken cancellationToken);

    /// <summary>Usage of the last <paramref name="days"/> UTC days, today included.</summary>
    Task<AdminUsageDto> GetUsageAsync(int days, CancellationToken cancellationToken);

    /// <summary>Newest first; <paramref name="rating"/> 1 or -1 filters, null shows both.</summary>
    Task<AdminFeedbackPageDto> GetFeedbackAsync(short? rating, int page, int pageSize, CancellationToken cancellationToken);
}

public sealed class AdminService(
    AppDbContext db,
    IRefreshTokenRepository refreshTokens,
    IUserAccessCache accessCache,
    IOptions<UsageOptions> usageOptions,
    TimeProvider time,
    ILogger<AdminService> logger) : IAdminService
{
    public const int MaxUsageDays = 90;
    private const int ExcerptLength = 400;

    private readonly UsageOptions _usage = usageOptions.Value;

    public async Task<AdminUserPageDto> GetUsersAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var users = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            users = users.Where(u => u.Email.Contains(term) || u.FullName.Contains(term));
        }

        var total = await users.CountAsync(cancellationToken);
        var rows = await users
            .OrderByDescending(u => u.CreatedAt).ThenByDescending(u => u.UserId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        var stats = await UserStatsAsync(rows.Select(u => u.UserId).ToList(), cancellationToken);

        return new AdminUserPageDto(rows.Select(u => ToDto(u, stats.GetValueOrDefault(u.UserId))).ToList(), total, page, pageSize);
    }

    public async Task<AdminUserDto> SetBlockedAsync(int adminUserId, int userId, bool blocked, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "User not found", "This account doesn't exist.",
                new Dictionary<string, object?> { ["reason"] = "user_not_found" });
        if (blocked && user.UserId == adminUserId)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "Can't block yourself",
                "You can't block your own account.", new Dictionary<string, object?> { ["reason"] = "cannot_block_self" });
        }
        if (blocked && user.Role == UserRoles.Admin)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "Can't block an admin",
                "Admins can't be blocked. Remove the admin role first.", new Dictionary<string, object?> { ["reason"] = "cannot_block_admin" });
        }

        if (user.IsBlocked != blocked)
        {
            user.IsBlocked = blocked;
            await db.SaveChangesAsync(cancellationToken);
            if (blocked)
            {
                // Signed-in tabs are refused at once (live check); revoking makes sure no session can be renewed.
                await refreshTokens.RevokeAllForUserAsync(userId, time.GetUtcNow().UtcDateTime, cancellationToken);
            }
            accessCache.Invalidate(userId);
            logger.LogInformation("Admin {AdminId} {Action} user {UserId}", adminUserId, blocked ? "blocked" : "unblocked", userId);
        }

        var stats = await UserStatsAsync([userId], cancellationToken);
        return ToDto(user, stats.GetValueOrDefault(userId));
    }

    public async Task<AdminUsageDto> GetUsageAsync(int days, CancellationToken cancellationToken)
    {
        var to = UsageService.Today(time);
        var from = to.AddDays(-(days - 1));
        var range = db.UsageDaily.AsNoTracking().Where(u => u.Date >= from && u.Date <= to);

        var byDay = (await range
                .GroupBy(u => u.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    Users = g.Count(),
                    Messages = g.Sum(u => u.MessageCount),
                    Input = g.Sum(u => u.InputTokens),
                    Output = g.Sum(u => u.OutputTokens),
                    Uploads = g.Sum(u => u.UploadCount),
                    Bytes = g.Sum(u => u.UploadBytes),
                })
                .ToListAsync(cancellationToken))
            .ToDictionary(d => d.Date);

        var dayRows = Enumerable.Range(0, days)
            .Select(i => from.AddDays(i))
            .Select(date => byDay.TryGetValue(date, out var d)
                ? new UsageDayDto(date, d.Users, d.Messages, d.Input, d.Output, d.Uploads, d.Bytes, Cost(d.Input, d.Output))
                : new UsageDayDto(date, 0, 0, 0, 0, 0, 0, 0))
            .ToList();

        var activeUsers = await range.Select(u => u.UserId).Distinct().CountAsync(cancellationToken);
        long input = dayRows.Sum(d => d.InputTokens), output = dayRows.Sum(d => d.OutputTokens);
        var totals = new UsageTotalsDto(activeUsers, dayRows.Sum(d => d.Messages), input, output,
            dayRows.Sum(d => d.Uploads), dayRows.Sum(d => d.UploadBytes), Cost(input, output));

        var top = await range
            .GroupBy(u => u.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                Messages = g.Sum(u => u.MessageCount),
                Input = g.Sum(u => u.InputTokens),
                Output = g.Sum(u => u.OutputTokens),
            })
            .OrderByDescending(g => g.Input + g.Output)
            .Take(10)
            .Join(db.Users, g => g.UserId, u => u.UserId, (g, u) => new { g.UserId, u.FullName, u.Email, g.Messages, g.Input, g.Output })
            .ToListAsync(cancellationToken);

        return new AdminUsageDto(from, to, dayRows, totals,
            top.OrderByDescending(t => t.Input + t.Output)
                .Select(t => new TopUserDto(t.UserId, t.FullName, t.Email, t.Messages, t.Input + t.Output, Cost(t.Input, t.Output)))
                .ToList(),
            new UsageLimitsDto(_usage.DailyTokenQuota, _usage.DailyUploadQuota,
                _usage.InputTokenPricePerMillion, _usage.OutputTokenPricePerMillion));
    }

    public async Task<AdminFeedbackPageDto> GetFeedbackAsync(short? rating, int page, int pageSize, CancellationToken cancellationToken)
    {
        var all = db.MessageFeedback.AsNoTracking();
        var helpful = await all.CountAsync(f => f.Rating > 0, cancellationToken);
        var notHelpful = await all.CountAsync(f => f.Rating < 0, cancellationToken);

        var filtered = rating is { } r ? all.Where(f => f.Rating == r) : all;
        var total = await filtered.CountAsync(cancellationToken);
        var rows = await filtered
            .OrderByDescending(f => f.CreatedAt).ThenByDescending(f => f.MessageFeedbackId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(f => new
            {
                f.MessageFeedbackId,
                f.Rating,
                f.Comment,
                f.CreatedAt,
                f.UserId,
                Email = db.Users.Where(u => u.UserId == f.UserId).Select(u => u.Email).FirstOrDefault(),
                f.MessageId,
                f.Message.ModelId,
                Reply = f.Message.Content,
                Question = db.Messages.Where(m => m.MessageId == f.Message.ParentMessageId).Select(m => m.Content).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return new AdminFeedbackPageDto(
            rows.Select(f => new AdminFeedbackDto(f.MessageFeedbackId, f.Rating, f.Comment, f.CreatedAt, f.UserId, f.Email ?? "",
                f.MessageId, f.ModelId, Excerpt(f.Question ?? ""), Excerpt(f.Reply))).ToList(),
            total, page, pageSize, helpful, notHelpful);
    }

    /// <summary>Estimated provider cost in US dollars, from the configured prices.</summary>
    private decimal Cost(long inputTokens, long outputTokens) =>
        Math.Round((inputTokens * _usage.InputTokenPricePerMillion + outputTokens * _usage.OutputTokenPricePerMillion) / 1_000_000m, 4);

    private sealed record UserStats(int Messages, long Tokens, int Uploads, DateTime? LastActiveOn);

    private async Task<Dictionary<int, UserStats>> UserStatsAsync(List<int> userIds, CancellationToken cancellationToken)
    {
        var today = UsageService.Today(time);
        var rows = await db.UsageDaily.AsNoTracking()
            .Where(u => userIds.Contains(u.UserId))
            .GroupBy(u => u.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                Messages = g.Where(u => u.Date == today).Sum(u => u.MessageCount),
                Tokens = g.Where(u => u.Date == today).Sum(u => u.InputTokens + u.OutputTokens),
                Uploads = g.Where(u => u.Date == today).Sum(u => u.UploadCount),
                LastActiveOn = g.Max(u => u.Date),
            })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.UserId, r => new UserStats(r.Messages, r.Tokens, r.Uploads, r.LastActiveOn));
    }

    private static AdminUserDto ToDto(User user, UserStats? stats) =>
        new(user.UserId, user.FullName, user.Email, user.Role, user.IsBlocked, user.CreatedAt,
            stats?.Messages ?? 0, stats?.Tokens ?? 0, stats?.Uploads ?? 0, stats?.LastActiveOn);

    private static string Excerpt(string text) =>
        text.Length <= ExcerptLength ? text : string.Concat(text.AsSpan(0, ExcerptLength).TrimEnd(), "…");
}
