using ChatGPTClone.Api.Data;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatGPTClone.Api.Repositories;

public sealed class OtpRepository(AppDbContext db) : IOtpRepository
{
    public Task<OtpVerification?> GetLatestActiveAsync(int userId, DateTime utcNow, CancellationToken cancellationToken) =>
        db.OtpVerifications
            .Where(o => o.UserId == userId && !o.IsUsed && o.ExpiresAt > utcNow)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<DateTime?> GetLatestCreatedAtAsync(int userId, CancellationToken cancellationToken) =>
        await db.OtpVerifications
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => (DateTime?)o.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<List<DateTime>> GetCreatedTimesSinceAsync(int userId, DateTime sinceUtc, CancellationToken cancellationToken) =>
        db.OtpVerifications
            .Where(o => o.UserId == userId && o.CreatedAt >= sinceUtc)
            .OrderBy(o => o.CreatedAt)
            .Select(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<int> InvalidateAllActiveAsync(int userId, CancellationToken cancellationToken) =>
        db.OtpVerifications
            .Where(o => o.UserId == userId && !o.IsUsed)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.IsUsed, true), cancellationToken);

    public async Task<bool> TryMarkUsedAsync(int otpVerificationId, CancellationToken cancellationToken) =>
        await db.OtpVerifications
            .Where(o => o.OtpVerificationId == otpVerificationId && !o.IsUsed)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.IsUsed, true), cancellationToken) == 1;

    public async Task<bool> TryIncrementAttemptAsync(int otpVerificationId, int maxAttempts, CancellationToken cancellationToken) =>
        await db.OtpVerifications
            .Where(o => o.OtpVerificationId == otpVerificationId && !o.IsUsed && o.AttemptCount < maxAttempts)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.AttemptCount, o => o.AttemptCount + 1), cancellationToken) == 1;

    public void Add(OtpVerification otp) => db.OtpVerifications.Add(otp);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
