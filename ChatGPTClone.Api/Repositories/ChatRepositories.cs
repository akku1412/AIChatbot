using ChatGPTClone.Api.Data;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Models;
using ChatGPTClone.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ChatGPTClone.Api.Repositories;

public sealed class ConversationRepository(AppDbContext db) : IConversationRepository
{
    public async Task<(List<Conversation> Items, int TotalCount)> ListAsync(int userId, int page, int pageSize, bool archived,
        CancellationToken cancellationToken)
    {
        var query = db.Conversations.AsNoTracking()
            .Where(c => c.UserId == userId && (archived ? c.ArchivedAt != null : c.ArchivedAt == null));
        var total = await query.CountAsync(cancellationToken);
        var ordered = archived
            ? query.OrderByDescending(c => c.ArchivedAt).ThenByDescending(c => c.ConversationId)
            : query.OrderByDescending(c => c.IsPinned).ThenByDescending(c => c.UpdatedAt).ThenByDescending(c => c.ConversationId);
        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<List<(Conversation Conversation, long? MatchingMessageId)>> SearchAsync(int userId, string term, int limit,
        CancellationToken cancellationToken)
    {
        // LIKE with the wildcards in the user's text escaped, so "50%" or "a_b" are searched literally.
        var pattern = "%" + term.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_").Replace("[", @"\[") + "%";
        var rows = await db.Conversations.AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => new
            {
                Conversation = c,
                MatchingMessageId = c.Messages
                    .Where(m => m.Role != MessageRole.System && EF.Functions.Like(m.Content, pattern, @"\"))
                    .Max(m => (long?)m.MessageId),
                TitleMatches = EF.Functions.Like(c.Title, pattern, @"\"),
            })
            .Where(x => x.TitleMatches || x.MatchingMessageId != null)
            .OrderByDescending(x => x.Conversation.IsPinned)
            .ThenByDescending(x => x.Conversation.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return rows.Select(r => (r.Conversation, r.MatchingMessageId)).ToList();
    }

    public Task<Conversation?> GetOwnedAsync(int conversationId, int userId, CancellationToken cancellationToken) =>
        db.Conversations.FirstOrDefaultAsync(c => c.ConversationId == conversationId && c.UserId == userId, cancellationToken);

    public void Add(Conversation conversation) => db.Conversations.Add(conversation);

    public void Remove(Conversation conversation) => db.Conversations.Remove(conversation);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

public sealed class MessageRepository(AppDbContext db) : IMessageRepository
{
    public Task<List<MessageNode>> GetNodesAsync(int conversationId, CancellationToken cancellationToken) =>
        db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.MessageId)
            .Select(m => new MessageNode(m.MessageId, m.ParentMessageId, m.Role, m.VersionIndex, m.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<List<Message>> GetByIdsAsync(IReadOnlyCollection<long> messageIds, CancellationToken cancellationToken)
    {
        if (messageIds.Count == 0)
        {
            return [];
        }
        return await db.Messages.AsNoTracking()
            .Include(m => m.Attachments).ThenInclude(a => a.File)
            .Where(m => messageIds.Contains(m.MessageId))
            .OrderBy(m => m.MessageId)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Guid>> GetAttachmentFileIdsAsync(IReadOnlyCollection<long> messageIds, int take,
        CancellationToken cancellationToken)
    {
        if (messageIds.Count == 0)
        {
            return [];
        }
        var rows = await db.MessageAttachments.AsNoTracking()
            .Where(a => messageIds.Contains(a.MessageId))
            .OrderByDescending(a => a.MessageId)
            .Select(a => a.FileId)
            .ToListAsync(cancellationToken);
        return rows.Distinct().Take(take).ToList();
    }

    public Task<Message?> GetOwnedAsync(long messageId, int userId, CancellationToken cancellationToken) =>
        db.Messages.AsNoTracking()
            .Include(m => m.Conversation)
            .Include(m => m.Attachments).ThenInclude(a => a.File)
            .AsSplitQuery()
            .FirstOrDefaultAsync(m => m.MessageId == messageId && m.Conversation.UserId == userId, cancellationToken);

    public Task<bool> AnyAsync(int conversationId, CancellationToken cancellationToken) =>
        db.Messages.AnyAsync(m => m.ConversationId == conversationId, cancellationToken);

    public void Add(Message message) => db.Messages.Add(message);

    public async Task ActivateAsync(int conversationId, long? parentMessageId, long activeMessageId, CancellationToken cancellationToken)
    {
        // One statement: the chosen version becomes active, its siblings inactive.
        await db.Messages
            .Where(m => m.ConversationId == conversationId && m.ParentMessageId == parentMessageId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsActive, m => m.MessageId == activeMessageId), cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
