namespace ChatGPTClone.Api.Models;

public class RefreshToken : IHasCreatedAt
{
    public int RefreshTokenId { get; set; }

    public int UserId { get; set; }

    /// <summary>SHA-256 hash of the random token. The raw token only ever lives in the user's HttpOnly cookie.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;

    public bool IsActive(DateTime utcNow) => RevokedAt is null && ExpiresAt > utcNow;
}
