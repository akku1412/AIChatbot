using System.Text.RegularExpressions;
using ChatGPTClone.Api.DTOs.Chat;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Middleware;
using ChatGPTClone.Api.Models;

namespace ChatGPTClone.Api.Services;

public interface IConversationService
{
    /// <summary>The main list (pinned first) or, with <paramref name="archived"/>, the archived chats.</summary>
    /// Only chats in <paramref name="projectId"/>; null = chats in no project.
    Task<PagedResult<ConversationDto>> ListAsync(int userId, int page, int pageSize, bool archived, int? projectId,
        CancellationToken cancellationToken);
    Task<ConversationDto> GetAsync(int userId, int conversationId, CancellationToken cancellationToken);
    /// <param name="modelId">An allow-listed model id (checked by the request validator); null = default.</param>
    /// <param name="projectId">One of the user's projects (404 otherwise); null = no project.</param>
    Task<ConversationDto> CreateAsync(int userId, string? title, string? modelId, int? projectId, CancellationToken cancellationToken);
    Task<ConversationDto> RenameAsync(int userId, int conversationId, string title, CancellationToken cancellationToken);

    /// <summary>Moves the chat into one of the user's projects, or back to the main list with null.</summary>
    Task<ConversationDto> MoveAsync(int userId, int conversationId, int? projectId, CancellationToken cancellationToken);

    /// <summary>Changes only the fields that are set: title, pinned, archived.</summary>
    Task<ConversationDto> PatchAsync(int userId, int conversationId, PatchConversationRequest request, CancellationToken cancellationToken);

    /// <summary>Chats whose title or messages contain the text, with a snippet around the match.</summary>
    Task<IReadOnlyList<ConversationSearchResultDto>> SearchAsync(int userId, string query, int limit, CancellationToken cancellationToken);

    Task DeleteAsync(int userId, int conversationId, CancellationToken cancellationToken);

    /// <summary>Loads a conversation owned by the user or throws 404 (never 403, so ids don't leak).</summary>
    Task<Conversation> GetOwnedOrThrowAsync(int userId, int conversationId, CancellationToken cancellationToken);
}

public sealed partial class ConversationService(IConversationRepository repository, IMessageRepository messages,
    IProjectService projects) : IConversationService
{
    public const string DefaultTitle = "New chat";

    public async Task<PagedResult<ConversationDto>> ListAsync(int userId, int page, int pageSize, bool archived, int? projectId,
        CancellationToken cancellationToken)
    {
        if (projectId is { } id)
        {
            await projects.EnsureOwnedAsync(userId, id, cancellationToken);
        }
        var (items, total) = await repository.ListAsync(userId, page, pageSize, archived, projectId, cancellationToken);
        return new PagedResult<ConversationDto>(
            items.Select(ConversationDto.From).ToList(), page, pageSize, total, page * pageSize < total);
    }

    public async Task<ConversationDto> GetAsync(int userId, int conversationId, CancellationToken cancellationToken) =>
        ConversationDto.From(await GetOwnedOrThrowAsync(userId, conversationId, cancellationToken));

    public async Task<ConversationDto> CreateAsync(int userId, string? title, string? modelId, int? projectId,
        CancellationToken cancellationToken)
    {
        if (projectId is { } id)
        {
            await projects.EnsureOwnedAsync(userId, id, cancellationToken);
        }
        var conversation = new Conversation
        {
            UserId = userId,
            Title = string.IsNullOrWhiteSpace(title) ? DefaultTitle : title.Trim(),
            ModelId = modelId,
            ProjectId = projectId,
        };
        repository.Add(conversation);
        await repository.SaveChangesAsync(cancellationToken);
        return ConversationDto.From(conversation);
    }

    public async Task<ConversationDto> RenameAsync(int userId, int conversationId, string title,
        CancellationToken cancellationToken)
    {
        var conversation = await GetOwnedOrThrowAsync(userId, conversationId, cancellationToken);
        conversation.Title = title.Trim();
        await repository.SaveChangesAsync(cancellationToken);
        return ConversationDto.From(conversation);
    }

    public async Task<ConversationDto> MoveAsync(int userId, int conversationId, int? projectId, CancellationToken cancellationToken)
    {
        var conversation = await GetOwnedOrThrowAsync(userId, conversationId, cancellationToken);
        if (projectId is { } id)
        {
            await projects.EnsureOwnedAsync(userId, id, cancellationToken); // another user's project: 404
        }
        conversation.ProjectId = projectId; // not "activity": the sidebar order is unchanged
        await repository.SaveChangesAsync(cancellationToken);
        return ConversationDto.From(conversation);
    }

    public async Task<ConversationDto> PatchAsync(int userId, int conversationId, PatchConversationRequest request,
        CancellationToken cancellationToken)
    {
        var conversation = await GetOwnedOrThrowAsync(userId, conversationId, cancellationToken);
        if (request.Title is { } title)
        {
            conversation.Title = title.Trim();
        }
        if (request.IsPinned is { } pinned)
        {
            conversation.IsPinned = pinned;
        }
        if (request.Archived is { } archived)
        {
            // Keep the original archive time when "archived" is sent again.
            conversation.ArchivedAt = archived ? conversation.ArchivedAt ?? DateTime.UtcNow : null;
        }
        if (request.ModelId is { } modelId)
        {
            conversation.ModelId = modelId; // allow-listed by the request validator; applies from the next reply
        }
        // Pinning/archiving/switching model is not "activity": UpdatedAt (sidebar order) is left unchanged.
        await repository.SaveChangesAsync(cancellationToken);
        return ConversationDto.From(conversation);
    }

    public async Task<IReadOnlyList<ConversationSearchResultDto>> SearchAsync(int userId, string query, int limit,
        CancellationToken cancellationToken)
    {
        var term = Whitespace().Replace(query.Trim(), " ");
        var hits = await repository.SearchAsync(userId, term, limit, cancellationToken);
        var messageIds = hits.Where(h => h.MatchingMessageId is not null).Select(h => h.MatchingMessageId!.Value).ToList();
        var contents = (await messages.GetByIdsAsync(messageIds, cancellationToken)).ToDictionary(m => m.MessageId, m => m.Content);

        return hits.Select(h => new ConversationSearchResultDto(
                h.Conversation.ConversationId,
                h.Conversation.Title,
                h.Conversation.UpdatedAt,
                h.Conversation.IsPinned,
                h.Conversation.ArchivedAt,
                h.Conversation.Title.Contains(term, StringComparison.OrdinalIgnoreCase),
                h.MatchingMessageId is { } id && contents.TryGetValue(id, out var content) ? Snippet(content, term) : null,
                h.MatchingMessageId,
                h.Conversation.ProjectId))
            .ToList();
    }

    public async Task DeleteAsync(int userId, int conversationId, CancellationToken cancellationToken)
    {
        var conversation = await GetOwnedOrThrowAsync(userId, conversationId, cancellationToken);
        repository.Remove(conversation); // Messages are removed by the database's cascade delete.
        await repository.SaveChangesAsync(cancellationToken);
    }

    public async Task<Conversation> GetOwnedOrThrowAsync(int userId, int conversationId, CancellationToken cancellationToken) =>
        await repository.GetOwnedAsync(conversationId, userId, cancellationToken)
        ?? throw new ApiException(StatusCodes.Status404NotFound, "Conversation not found",
            "This conversation doesn't exist or has been deleted.");

    /// <summary>~160 characters around the first match, on word boundaries, on one line, with "…" where cut.</summary>
    internal static string Snippet(string content, string term, int before = 60, int after = 100)
    {
        var flat = Whitespace().Replace(content, " ").Trim();
        var index = flat.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return flat.Length <= before + after ? flat : flat[..(before + after)].TrimEnd() + "…";
        }

        var start = Math.Max(0, index - before);
        var end = Math.Min(flat.Length, index + term.Length + after);

        // Don't cut words in half: move the start forward / the end back to a space, but never into the match.
        var firstSpace = start > 0 ? flat.IndexOf(' ', start) : -1;
        if (firstSpace >= 0 && firstSpace < index)
        {
            start = firstSpace + 1;
        }
        var lastSpace = end < flat.Length ? flat.LastIndexOf(' ', end) : -1;
        if (lastSpace >= index + term.Length)
        {
            end = lastSpace;
        }
        return (start > 0 ? "…" : "") + flat[start..end].Trim() + (end < flat.Length ? "…" : "");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
