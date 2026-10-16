using ChatGPTClone.Api.Data;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatGPTClone.Api.Repositories;

public sealed class ConversationRepository(AppDbContext db) : IConversationRepository
{
    public async Task<(List<Conversation> Items, int TotalCount)> ListAsync(int userId, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        var query = db.Conversations.AsNoTracking().Where(c => c.UserId == userId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(c => c.UpdatedAt)
            .ThenByDescending(c => c.ConversationId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<Conversation?> GetOwnedAsync(int conversationId, int userId, CancellationToken cancellationToken) =>
        db.Conversations.FirstOrDefaultAsync(c => c.ConversationId == conversationId && c.UserId == userId, cancellationToken);

    public void Add(Conversation conversation) => db.Conversations.Add(conversation);

    public void Remove(Conversation conversation) => db.Conversations.Remove(conversation);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

public sealed class MessageRepository(AppDbContext db) : IMessageRepository
{
    public async Task<(List<Message> Items, bool HasMore)> GetPageAsync(int conversationId, long? beforeMessageId,
        int limit, CancellationToken cancellationToken)
    {
        var query = db.Messages.AsNoTracking()
            .Include(m => m.Attachments).ThenInclude(a => a.File)
            .Where(m => m.ConversationId == conversationId);
        if (beforeMessageId is { } before)
        {
            query = query.Where(m => m.MessageId < before);
        }

        // Newest first + one extra row to know whether older messages exist; then flip to oldest-first.
        var newestFirst = await query
            .OrderByDescending(m => m.MessageId)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = newestFirst.Count > limit;
        var page = newestFirst.Take(limit).Reverse().ToList();
        return (page, hasMore);
    }

    public async Task<List<Message>> GetRecentAsync(int conversationId, int count, CancellationToken cancellationToken)
    {
        var newestFirst = await db.Messages.AsNoTracking()
            .Include(m => m.Attachments).ThenInclude(a => a.File)
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.MessageId)
            .Take(count)
            .ToListAsync(cancellationToken);
        newestFirst.Reverse();
        return newestFirst;
    }

    public async Task<List<Guid>> GetRecentAttachmentFileIdsAsync(int conversationId, int take, CancellationToken cancellationToken)
    {
        var rows = await db.MessageAttachments.AsNoTracking()
            .Where(a => a.Message.ConversationId == conversationId)
            .OrderByDescending(a => a.MessageId)
            .Select(a => a.FileId)
            .Take(take * 4)
            .ToListAsync(cancellationToken);
        return rows.Distinct().Take(take).ToList();
    }

    public Task<bool> AnyAsync(int conversationId, CancellationToken cancellationToken) =>
        db.Messages.AnyAsync(m => m.ConversationId == conversationId, cancellationToken);

    public void Add(Message message) => db.Messages.Add(message);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
