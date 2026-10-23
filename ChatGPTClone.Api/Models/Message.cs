namespace ChatGPTClone.Api.Models;

public enum MessageRole
{
    User,
    Assistant,
    System,
}

public class Message : IHasCreatedAt
{
    public long MessageId { get; set; }

    public int ConversationId { get; set; }

    /// <summary>Stored as a lower-case string ("user", "assistant", "system").</summary>
    public MessageRole Role { get; set; }

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>Total tokens reported by the AI provider for assistant messages; null for user messages.</summary>
    public int? TokenUsage { get; set; }

    /// <summary>
    /// The message this one follows (null for the first message). Messages form a tree: a regenerated reply or an
    /// edited question is a sibling (same parent) of the version it replaces.
    /// </summary>
    public long? ParentMessageId { get; set; }

    /// <summary>1-based position among its siblings (version "2/3").</summary>
    public int VersionIndex { get; set; } = 1;

    /// <summary>The version currently shown among its siblings. The thread follows active versions from the root.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>The reply was stopped by the user before it finished; Content holds the part that was generated.</summary>
    public bool IsTruncated { get; set; }

    public Message? ParentMessage { get; set; }

    public Conversation Conversation { get; set; } = null!;

    public ICollection<MessageAttachment> Attachments { get; set; } = new List<MessageAttachment>();
}
