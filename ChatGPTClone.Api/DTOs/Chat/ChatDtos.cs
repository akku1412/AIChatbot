using ChatGPTClone.Api.DTOs.Files;
using ChatGPTClone.Api.Models;

namespace ChatGPTClone.Api.DTOs.Chat;

public sealed record ConversationDto(int ConversationId, string Title, DateTime CreatedAt, DateTime UpdatedAt,
    bool IsPinned, DateTime? ArchivedAt)
{
    public static ConversationDto From(Conversation c) =>
        new(c.ConversationId, c.Title, c.CreatedAt, c.UpdatedAt, c.IsPinned, c.ArchivedAt);
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, bool HasMore);

/// <summary>
/// Role is serialized lower-case ("user" / "assistant") to match the database and the Angular models.
/// VersionNumber/VersionCount drive the "‹ 2/3 ›" arrows; IsTruncated means the user stopped the reply.
/// Sources (AI replies) are the passages it cited as "[file:ChunkId]" that were really in its prompt (invented ids are dropped).
/// </summary>
public sealed record MessageDto(long MessageId, string Role, string Content, DateTime CreatedAt, int? TokenUsage,
    IReadOnlyList<AttachmentDto> Attachments, long? ParentMessageId, int VersionNumber, int VersionCount, bool IsTruncated,
    IReadOnlyList<SourceDto> Sources)
{
    public static MessageDto From(Message m, int versionNumber = 1, int versionCount = 1) =>
        new(m.MessageId, m.Role.ToString().ToLowerInvariant(), m.Content, m.CreatedAt, m.TokenUsage,
            m.Attachments
                .Where(a => a.File is not null)
                .Select(a => new AttachmentDto(a.FileId, a.File.OriginalFileName, a.File.FileExtension, a.File.FileSize, a.File.IsImage))
                .ToList(),
            m.ParentMessageId, versionNumber, versionCount, m.IsTruncated,
            m.Sources
                .Where(s => s.Chunk?.File is not null)
                .OrderByDescending(s => s.Score)
                .Select(s => new SourceDto(s.ChunkId, s.Chunk.FileId, s.Chunk.File.OriginalFileName, s.Chunk.Location, s.Chunk.Source == ChunkSource.Ocr))
                .ToList());
}

/// <summary>Oldest-first page of messages; HasMore means older messages exist (use "before").</summary>
public sealed record MessagePageDto(IReadOnlyList<MessageDto> Items, bool HasMore);

public sealed record SendMessageResponse(MessageDto UserMessage, MessageDto AssistantMessage, ConversationDto Conversation);

/// <summary>
/// The final "done" event of a streamed reply. AssistantMessage is null when generation was stopped before any text.
/// ReplacedMessageId is the message this turn took the place of on screen: the previous reply (regenerate) or the
/// edited question (edit). It is kept as an earlier version, reachable with the version arrows.
/// </summary>
public sealed record StreamTurnResult(
    MessageDto UserMessage,
    MessageDto? AssistantMessage,
    ConversationDto Conversation,
    bool Stopped,
    bool Truncated,
    long? ReplacedMessageId);

/// <summary>One search hit: the conversation and where the words were found.</summary>
public sealed record ConversationSearchResultDto(
    int ConversationId,
    string Title,
    DateTime UpdatedAt,
    bool IsPinned,
    DateTime? ArchivedAt,
    bool TitleMatches,
    string? Snippet,
    long? MessageId);

// ----- Requests -----

public sealed class ListConversationsQuery
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;

    /// <summary>false (default): the main list; true: only archived chats.</summary>
    public bool Archived { get; init; }
}

public sealed class MessagesQuery
{
    /// <summary>Return messages older than this id (for "load earlier").</summary>
    public long? Before { get; init; }
    public int Limit { get; init; } = 50;
}

public sealed class SearchConversationsQuery
{
    public string Q { get; init; } = string.Empty;
    public int Limit { get; init; } = 20;
}

public sealed record CreateConversationRequest(string? Title);

public sealed record RenameConversationRequest(string Title);

/// <summary>Partial update: only the fields that are present change.</summary>
public sealed record PatchConversationRequest(string? Title, bool? IsPinned, bool? Archived);

/// <summary>Content may be empty when files are attached (the AI is then asked to summarize them).</summary>
public sealed record SendMessageRequest(string? Content, IReadOnlyList<Guid>? AttachmentIds);

/// <summary>Edit and resend: the new text of a user message.</summary>
public sealed record EditMessageRequest(string Content);

/// <summary>Show another version of a message (1-based, as in "2/3").</summary>
public sealed record SwitchVersionRequest(int VersionNumber);
