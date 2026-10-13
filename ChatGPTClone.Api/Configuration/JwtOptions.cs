using System.ComponentModel.DataAnnotations;

namespace ChatGPTClone.Api.Configuration;

/// <summary>Bound from "Jwt". SigningKey is a secret (user secrets / environment variable).</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    /// <summary>HMAC-SHA256 key, base64, at least 32 random bytes.</summary>
    [Required, MinLength(44)]
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>Short-lived access token; the refresh cookie renews it silently.</summary>
    [Range(1, 60)]
    public int AccessTokenMinutes { get; init; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; init; } = 7;
}
