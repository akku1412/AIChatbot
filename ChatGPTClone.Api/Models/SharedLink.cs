namespace ChatGPTClone.Api.Models;

/// <summary>
/// A read-only public link to a snapshot of a conversation. Only the SHA-256 hash of the random token is stored, so a
/// database leak doesn't reveal working links (and the link can't be shown again: sharing anew replaces it).
/// </summary>
public class SharedLink : IHasCreatedAt
{
    public int SharedLinkId { get; set; }

    public int ConversationId { get; set; }

    /// <summary>SHA-256 of the 32 random token bytes.</summary>
    public byte[] TokenHash { get; set; } = [];

    /// <summary>The title at share time (the snapshot doesn't follow later renames).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The visible thread at share time as JSON: later messages stay private.</summary>
    public string SnapshotJson { get; set; } = string.Empty;

    public int MessageCount { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Set when the owner stops sharing (or shares again, which replaces the link).</summary>
    public DateTime? RevokedAt { get; set; }

    public Conversation Conversation { get; set; } = null!;
}
