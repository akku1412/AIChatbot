using ChatGPTClone.Api.DTOs.Files;
using ChatGPTClone.Api.Models;

namespace ChatGPTClone.Api.DTOs.Chat;

public sealed record ConversationDto(int ConversationId, string Title, DateTime CreatedAt, DateTime UpdatedAt)
{
    public static ConversationDto From(Conversation c) => new(c.ConversationId, c.Title, c.CreatedAt, c.UpdatedAt);
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, bool HasMore);

/// <summary>Role is serialized lower-case ("user" / "assistant") to match the database and the Angular models.</summary>
public sealed record MessageDto(long MessageId, string Role, string Content, DateTime CreatedAt, int? TokenUsage,
    IReadOnlyList<AttachmentDto> Attachments)
{
    public static MessageDto From(Message m) =>
        new(m.MessageId, m.Role.ToString().ToLowerInvariant(), m.Content, m.CreatedAt, m.TokenUsage,
            m.Attachments
                .Where(a => a.File is not null)
                .Select(a => new AttachmentDto(a.FileId, a.File.OriginalFileName, a.File.FileExtension, a.File.FileSize))
                .ToList());
}

/// <summary>Oldest-first page of messages; HasMore means older messages exist (use "before").</summary>
public sealed record MessagePageDto(IReadOnlyList<MessageDto> Items, bool HasMore);

public sealed record SendMessageResponse(MessageDto UserMessage, MessageDto AssistantMessage, ConversationDto Conversation);

// ----- Requests -----

public sealed class ListConversationsQuery
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed class MessagesQuery
{
    /// <summary>Return messages older than this id (for "load earlier").</summary>
    public long? Before { get; init; }
    public int Limit { get; init; } = 50;
}

public sealed record CreateConversationRequest(string? Title);

public sealed record RenameConversationRequest(string Title);

/// <summary>Content may be empty when files are attached (the AI is then asked to summarize them).</summary>
public sealed record SendMessageRequest(string? Content, IReadOnlyList<Guid>? AttachmentIds);
