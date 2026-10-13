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

    public Conversation Conversation { get; set; } = null!;
}
