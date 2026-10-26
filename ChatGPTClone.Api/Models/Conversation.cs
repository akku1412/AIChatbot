namespace ChatGPTClone.Api.Models;

public class Conversation : IAuditable
{
    public int ConversationId { get; set; }

    public int UserId { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>Bumped whenever a message is added, so the sidebar can sort by recent activity.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Pinned chats are listed first.</summary>
    public bool IsPinned { get; set; }

    /// <summary>When the chat was archived (hidden from the main list); null = not archived.</summary>
    public DateTime? ArchivedAt { get; set; }

    /// <summary>The model picked for this chat (an Ai:Models id); null = the default model.</summary>
    public string? ModelId { get; set; }

    /// <summary>The project this chat belongs to; null = a chat in the main list.</summary>
    public int? ProjectId { get; set; }
    public Project? Project { get; set; }

    public User User { get; set; } = null!;
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
