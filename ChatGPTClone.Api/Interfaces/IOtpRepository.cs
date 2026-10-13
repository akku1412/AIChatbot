using ChatGPTClone.Api.Models;

namespace ChatGPTClone.Api.Interfaces;

public interface IOtpRepository
{
    /// <summary>Newest code that is not used and not expired, or null.</summary>
    Task<OtpVerification?> GetLatestActiveAsync(int userId, DateTime utcNow, CancellationToken cancellationToken);

    /// <summary>CreatedAt of the newest code (used or not), for the resend cooldown.</summary>
    Task<DateTime?> GetLatestCreatedAtAsync(int userId, CancellationToken cancellationToken);

    /// <summary>CreatedAt of codes issued since <paramref name="sinceUtc"/>, oldest first, for the hourly cap.</summary>
    Task<List<DateTime>> GetCreatedTimesSinceAsync(int userId, DateTime sinceUtc, CancellationToken cancellationToken);

    /// <summary>Marks every unused code of the user as used. Returns rows affected.</summary>
    Task<int> InvalidateAllActiveAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Atomically marks one code used. False if it was already used (e.g. a concurrent request won).</summary>
    Task<bool> TryMarkUsedAsync(int otpVerificationId, CancellationToken cancellationToken);

    /// <summary>Atomically adds one wrong attempt while the code is unused and under the limit.</summary>
    Task<bool> TryIncrementAttemptAsync(int otpVerificationId, int maxAttempts, CancellationToken cancellationToken);

    void Add(OtpVerification otp);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
