using System.Security.Cryptography;
using System.Text;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace ChatGPTClone.Api.Authentication;

/// <summary>
/// Opaque refresh tokens with rotation. The raw value only ever exists in the user's HttpOnly cookie;
/// the database keeps its SHA-256 hash. Every refresh revokes the presented token and issues a new one.
/// Presenting an already-revoked token (outside a short race window) is treated as theft and revokes
/// all of that user's sessions.
/// </summary>
public sealed class RefreshTokenService(
    IRefreshTokenRepository repository,
    IOptions<JwtOptions> options,
    TimeProvider timeProvider,
    ILogger<RefreshTokenService> logger) : IRefreshTokenService
{
    /// <summary>Two tabs refreshing at the same moment both present the same token; that is not theft.</summary>
    private static readonly TimeSpan ConcurrentRefreshGrace = TimeSpan.FromSeconds(30);

    public async Task<IssuedRefreshToken> IssueAsync(int userId, CancellationToken cancellationToken)
    {
        var raw = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
        var expiresAt = timeProvider.GetUtcNow().UtcDateTime.AddDays(options.Value.RefreshTokenDays);

        repository.Add(new RefreshToken { UserId = userId, TokenHash = Hash(raw), ExpiresAt = expiresAt });
        await repository.SaveChangesAsync(cancellationToken);

        return new IssuedRefreshToken(raw, expiresAt);
    }

    public async Task<RotatedRefreshToken?> RotateAsync(string rawToken, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var stored = await repository.GetByHashAsync(Hash(rawToken), cancellationToken);
        if (stored is null)
        {
            return null;
        }

        if (stored.RevokedAt is not null)
        {
            if (now - stored.RevokedAt.Value > ConcurrentRefreshGrace)
            {
                var revoked = await repository.RevokeAllForUserAsync(stored.UserId, now, cancellationToken);
                logger.LogWarning(
                    "Revoked refresh token {RefreshTokenId} was reused for user {UserId}; revoked {Count} active sessions",
                    stored.RefreshTokenId, stored.UserId, revoked);
            }
            return null;
        }

        if (stored.ExpiresAt <= now)
        {
            return null;
        }

        // Atomic: if another request rotated this token a moment ago, this one loses.
        if (!await repository.TryRevokeAsync(stored.RefreshTokenId, now, cancellationToken))
        {
            return null;
        }

        var next = await IssueAsync(stored.UserId, cancellationToken);
        return new RotatedRefreshToken(stored.User, next);
    }

    public async Task RevokeAsync(string rawToken, CancellationToken cancellationToken)
    {
        var stored = await repository.GetByHashAsync(Hash(rawToken), cancellationToken);
        if (stored is not null)
        {
            await repository.TryRevokeAsync(stored.RefreshTokenId, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        }
    }

    private static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
