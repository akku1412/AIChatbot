namespace ChatGPTClone.Api.Models;

public class Conversation : IAuditable
{
    public int ConversationId { get; set; }

    public int UserId { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>Bumped whenever a message is added, so the sidebar can sort by recent activity.</summary>
    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
