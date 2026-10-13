using ChatGPTClone.Api.DTOs.Chat;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Middleware;
using ChatGPTClone.Api.Models;

namespace ChatGPTClone.Api.Services;

public interface IConversationService
{
    Task<PagedResult<ConversationDto>> ListAsync(int userId, int page, int pageSize, CancellationToken cancellationToken);
    Task<ConversationDto> GetAsync(int userId, int conversationId, CancellationToken cancellationToken);
    Task<ConversationDto> CreateAsync(int userId, string? title, CancellationToken cancellationToken);
    Task<ConversationDto> RenameAsync(int userId, int conversationId, string title, CancellationToken cancellationToken);
    Task DeleteAsync(int userId, int conversationId, CancellationToken cancellationToken);

    /// <summary>Loads a conversation owned by the user or throws 404 (never 403, so ids don't leak).</summary>
    Task<Conversation> GetOwnedOrThrowAsync(int userId, int conversationId, CancellationToken cancellationToken);
}

public sealed class ConversationService(IConversationRepository repository) : IConversationService
{
    public const string DefaultTitle = "New chat";

    public async Task<PagedResult<ConversationDto>> ListAsync(int userId, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        var (items, total) = await repository.ListAsync(userId, page, pageSize, cancellationToken);
        return new PagedResult<ConversationDto>(
            items.Select(ConversationDto.From).ToList(), page, pageSize, total, page * pageSize < total);
    }

    public async Task<ConversationDto> GetAsync(int userId, int conversationId, CancellationToken cancellationToken) =>
        ConversationDto.From(await GetOwnedOrThrowAsync(userId, conversationId, cancellationToken));

    public async Task<ConversationDto> CreateAsync(int userId, string? title, CancellationToken cancellationToken)
    {
        var conversation = new Conversation
        {
            UserId = userId,
            Title = string.IsNullOrWhiteSpace(title) ? DefaultTitle : title.Trim(),
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
}
