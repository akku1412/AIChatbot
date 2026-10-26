using ChatGPTClone.Api.Data;
using ChatGPTClone.Api.DTOs.Chat;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Middleware;
using ChatGPTClone.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatGPTClone.Api.Services;

/// <summary>Thumbs up/down on AI replies, by the conversation's owner (one rating per reply, changeable).</summary>
public interface IFeedbackService
{
    Task<MessageFeedbackDto> RateAsync(int userId, long messageId, MessageFeedbackRequest request, CancellationToken cancellationToken);

    /// <summary>Takes the rating back (idempotent).</summary>
    Task RemoveAsync(int userId, long messageId, CancellationToken cancellationToken);
}

public sealed class FeedbackService(AppDbContext db, IMessageRepository messages, ILogger<FeedbackService> logger) : IFeedbackService
{
    public async Task<MessageFeedbackDto> RateAsync(int userId, long messageId, MessageFeedbackRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureRateableAsync(userId, messageId, cancellationToken);
        var rating = (short)(request.Rating == "up" ? 1 : -1);
        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();

        var feedback = await db.MessageFeedback.FirstOrDefaultAsync(f => f.MessageId == messageId, cancellationToken);
        if (feedback is null)
        {
            feedback = new MessageFeedback { MessageId = messageId, UserId = userId };
            db.MessageFeedback.Add(feedback);
        }
        feedback.Rating = rating;
        feedback.Comment = comment;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (feedback.MessageFeedbackId == 0)
        {
            // Two clicks raced to create it: the other one won; update that row instead.
            db.ChangeTracker.Clear();
            await db.MessageFeedback.Where(f => f.MessageId == messageId)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.Rating, rating).SetProperty(f => f.Comment, comment)
                    .SetProperty(f => f.UpdatedAt, DateTime.UtcNow), cancellationToken);
        }
        logger.LogInformation("Message {MessageId} rated {Rating} by user {UserId}", messageId, request.Rating, userId);
        return new MessageFeedbackDto(request.Rating, comment);
    }

    public async Task RemoveAsync(int userId, long messageId, CancellationToken cancellationToken)
    {
        await EnsureRateableAsync(userId, messageId, cancellationToken);
        await db.MessageFeedback.Where(f => f.MessageId == messageId).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>404 unless the message is the user's; 409 for messages that aren't AI replies.</summary>
    private async Task EnsureRateableAsync(int userId, long messageId, CancellationToken cancellationToken)
    {
        var message = await messages.GetOwnedAsync(messageId, userId, cancellationToken);
        if (message is null || message.Role == MessageRole.System)
        {
            throw new ApiException(StatusCodes.Status404NotFound, "Message not found",
                "This message doesn't exist or has been deleted.", new Dictionary<string, object?> { ["reason"] = "message_not_found" });
        }
        if (message.Role != MessageRole.Assistant)
        {
            throw new ApiException(StatusCodes.Status409Conflict, "Can't rate this message", "Only AI replies can be rated.",
                new Dictionary<string, object?> { ["reason"] = "not_rateable" });
        }
    }
}
