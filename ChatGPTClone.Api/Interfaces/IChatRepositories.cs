using ChatGPTClone.Api.Models;

namespace ChatGPTClone.Api.Interfaces;

/// <summary>Every read/write is scoped by userId, so an id from another user simply isn't found.</summary>
public interface IConversationRepository
{
    Task<(List<Conversation> Items, int TotalCount)> ListAsync(int userId, int page, int pageSize,
        CancellationToken cancellationToken);

    Task<Conversation?> GetOwnedAsync(int conversationId, int userId, CancellationToken cancellationToken);

    void Add(Conversation conversation);
    void Remove(Conversation conversation);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IMessageRepository
{
    /// <summary>Up to <paramref name="limit"/> messages older than <paramref name="beforeMessageId"/>, oldest first.</summary>
    Task<(List<Message> Items, bool HasMore)> GetPageAsync(int conversationId, long? beforeMessageId, int limit,
        CancellationToken cancellationToken);

    /// <summary>The most recent messages for AI context, oldest first.</summary>
    Task<List<Message>> GetRecentAsync(int conversationId, int count, CancellationToken cancellationToken);

    /// <summary>Every message with its attachments, oldest first (for exporting the whole conversation).</summary>
    Task<List<Message>> GetAllAsync(int conversationId, CancellationToken cancellationToken);

    Task<bool> AnyAsync(int conversationId, CancellationToken cancellationToken);

    /// <summary>Distinct ids of files attached in this conversation, most recently attached first.</summary>
    Task<List<Guid>> GetRecentAttachmentFileIdsAsync(int conversationId, int take, CancellationToken cancellationToken);

    void Add(Message message);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
