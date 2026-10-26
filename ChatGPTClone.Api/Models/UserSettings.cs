namespace ChatGPTClone.Api.Models;

/// <summary>
/// Per-user preferences (one row per user, created on first save). AboutMe and ResponseStyle are the
/// "custom instructions" added to every chat's system prompt, below the app's own rules.
/// </summary>
public class UserSettings : IAuditable
{
    public const int MaxInstructionLength = 1500;

    public int UserId { get; set; }

    /// <summary>"What should AIChat know about you?"</summary>
    public string? AboutMe { get; set; }

    /// <summary>"How should AIChat respond?"</summary>
    public string? ResponseStyle { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}
