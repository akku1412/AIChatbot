using ChatGPTClone.Api.Models;

namespace ChatGPTClone.Api.Interfaces;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>Atomically revokes one token. False if it was already revoked (a concurrent refresh won).</summary>
    Task<bool> TryRevokeAsync(int refreshTokenId, DateTime revokedAtUtc, CancellationToken cancellationToken);

    /// <summary>Revokes every active token of the user (logout everywhere / theft response).</summary>
    Task<int> RevokeAllForUserAsync(int userId, DateTime revokedAtUtc, CancellationToken cancellationToken);

    void Add(RefreshToken refreshToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
