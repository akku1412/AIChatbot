using ChatGPTClone.Api.Models;
using ChatGPTClone.Api.Services;

namespace ChatGPTClone.Api.Interfaces;

/// <summary>Every read/write is scoped by userId, so an id from another user simply isn't found.</summary>
public interface IConversationRepository
{
    /// <summary>The main list (pinned first, then most recent) or, with <paramref name="archived"/>, the archived chats.
    /// Only chats in <paramref name="projectId"/> (null = chats in no project).</summary>
    Task<(List<Conversation> Items, int TotalCount)> ListAsync(int userId, int page, int pageSize, bool archived,
        int? projectId, CancellationToken cancellationToken);

    /// <summary>
    /// Conversations whose title or any message contains <paramref name="term"/> (case-insensitive LIKE), pinned first,
    /// then most recent; with the id of the newest matching message, if any.
    /// </summary>
    Task<List<(Conversation Conversation, long? MatchingMessageId)>> SearchAsync(int userId, string term, int limit,
        CancellationToken cancellationToken);

    Task<Conversation?> GetOwnedAsync(int conversationId, int userId, CancellationToken cancellationToken);

    void Add(Conversation conversation);
    void Remove(Conversation conversation);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IMessageRepository
{
    /// <summary>Every message of the conversation as a light node (no text), oldest first: the branch structure.</summary>
    Task<List<MessageNode>> GetNodesAsync(int conversationId, CancellationToken cancellationToken);

    /// <summary>These messages with their attachments, oldest first.</summary>
    Task<List<Message>> GetByIdsAsync(IReadOnlyCollection<long> messageIds, CancellationToken cancellationToken);

    /// <summary>One message with its conversation and attachments, only if the conversation belongs to <paramref name="userId"/>.</summary>
    Task<Message?> GetOwnedAsync(long messageId, int userId, CancellationToken cancellationToken);

    Task<bool> AnyAsync(int conversationId, CancellationToken cancellationToken);

    /// <summary>Distinct ids of files attached to these messages, most recently attached first.</summary>
    Task<List<Guid>> GetAttachmentFileIdsAsync(IReadOnlyCollection<long> messageIds, int take, CancellationToken cancellationToken);

    void Add(Message message);

    /// <summary>Makes one version active and its siblings (same parent) inactive.</summary>
    Task ActivateAsync(int conversationId, long? parentMessageId, long activeMessageId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
