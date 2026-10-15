using System.ComponentModel.DataAnnotations;

namespace ChatGPTClone.Api.Configuration;

/// <summary>Bound from "RateLimits". Requests per minute; defaults suit production.</summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    /// <summary>Every request, per client IP.</summary>
    [Range(1, 100_000)]
    public int GlobalPerMinute { get; init; } = 100;

    /// <summary>Google sign-in and OTP endpoints, per client IP.</summary>
    [Range(1, 100_000)]
    public int AuthPerMinute { get; init; } = 10;

    /// <summary>AI messages, per signed-in user.</summary>
    [Range(1, 100_000)]
    public int AiChatPerMinute { get; init; } = 20;
}
