using System.ComponentModel.DataAnnotations;

namespace ChatGPTClone.Api.Configuration;

/// <summary>Bound from "Otp". HashKey is a secret (user secrets / environment variable).</summary>
public sealed class OtpOptions
{
    public const string SectionName = "Otp";

    [Range(1, 60)]
    public int ExpiryMinutes { get; init; } = 5;

    [Range(10, 3600)]
    public int ResendCooldownSeconds { get; init; } = 60;

    /// <summary>Wrong guesses allowed per code before it is burned.</summary>
    [Range(1, 20)]
    public int MaxAttempts { get; init; } = 5;

    /// <summary>Codes a single user may request within a rolling hour.</summary>
    [Range(1, 100)]
    public int MaxSendsPerHour { get; init; } = 5;

    /// <summary>How long the challenge token from /api/auth/google stays valid.</summary>
    [Range(5, 60)]
    public int ChallengeLifetimeMinutes { get; init; } = 15;

    /// <summary>Base64 HMAC key (32+ random bytes). Without it, a leaked OtpHash column could be brute-forced.</summary>
    [Required, MinLength(32)]
    public string HashKey { get; init; } = string.Empty;
}
