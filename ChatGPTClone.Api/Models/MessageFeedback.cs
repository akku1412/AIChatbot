namespace ChatGPTClone.Api.Models;

/// <summary>A thumbs up/down (and optional comment) on an AI reply, by the conversation's owner. One per reply.</summary>
public class MessageFeedback : IAuditable
{
    public const int CommentMaxLength = 1000;

    public int MessageFeedbackId { get; set; }

    public long MessageId { get; set; }

    /// <summary>Who rated (the conversation's owner). For the admin panel's reports; no second FK (cascade rule).</summary>
    public int UserId { get; set; }

    /// <summary>+1 = helpful, -1 = not helpful.</summary>
    public short Rating { get; set; }

    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Message Message { get; set; } = null!;
}
