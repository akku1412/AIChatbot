using System.Security.Cryptography;
using System.Text.Json;
using ChatGPTClone.Api.Data;
using ChatGPTClone.Api.DTOs.Sharing;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Middleware;
using ChatGPTClone.Api.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace ChatGPTClone.Api.Services;

/// <summary>Read-only public links to a snapshot of a conversation.</summary>
public interface ISharingService
{
    /// <summary>Snapshots the visible thread and returns a new link token; any earlier link for the chat stops working.</summary>
    Task<ShareCreatedDto> ShareAsync(int userId, int conversationId, CancellationToken cancellationToken);

    Task<ShareStatusDto> GetStatusAsync(int userId, int conversationId, CancellationToken cancellationToken);

    /// <summary>Stops sharing (idempotent).</summary>
    Task RevokeAsync(int userId, int conversationId, CancellationToken cancellationToken);

    /// <summary>The snapshot behind a live token; null for malformed, unknown or revoked tokens alike.</summary>
    Task<SharedConversationDto?> GetSharedAsync(string token, CancellationToken cancellationToken);
}

public sealed class SharingService(
    AppDbContext db,
    IConversationService conversations,
    IMessageRepository messages,
    TimeProvider clock,
    ILogger<SharingService> logger) : ISharingService
{
    private const int TokenBytes = 32;
    private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web);

    public async Task<ShareCreatedDto> ShareAsync(int userId, int conversationId, CancellationToken cancellationToken)
    {
        var conversation = await conversations.GetOwnedOrThrowAsync(userId, conversationId, cancellationToken);
        var snapshot = await SnapshotAsync(conversationId, cancellationToken);
        if (snapshot.Count == 0)
        {
            throw new ApiException(StatusCodes.Status409Conflict, "Nothing to share yet",
                "Send a message first: an empty chat can't be shared.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var token = RandomNumberGenerator.GetBytes(TokenBytes);
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            // One live link per chat: sharing again replaces (revokes) the previous link.
            await db.SharedLinks.Where(s => s.ConversationId == conversationId && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.RevokedAt, now), cancellationToken);
            db.SharedLinks.Add(new SharedLink
            {
                ConversationId = conversationId,
                TokenHash = SHA256.HashData(token),
                Title = conversation.Title,
                SnapshotJson = JsonSerializer.Serialize(snapshot, SnapshotJson),
                MessageCount = snapshot.Count,
                CreatedAt = now,
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
        logger.LogInformation("Conversation {ConversationId} shared by user {UserId} ({Count} messages)", conversationId, userId, snapshot.Count);
        return new ShareCreatedDto(WebEncoders.Base64UrlEncode(token), now, snapshot.Count);
    }

    public async Task<ShareStatusDto> GetStatusAsync(int userId, int conversationId, CancellationToken cancellationToken)
    {
        await conversations.GetOwnedOrThrowAsync(userId, conversationId, cancellationToken);
        var live = await db.SharedLinks.AsNoTracking()
            .Where(s => s.ConversationId == conversationId && s.RevokedAt == null)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new { s.CreatedAt, s.MessageCount })
            .FirstOrDefaultAsync(cancellationToken);
        return live is null ? new ShareStatusDto(false, null, null) : new ShareStatusDto(true, live.CreatedAt, live.MessageCount);
    }

    public async Task RevokeAsync(int userId, int conversationId, CancellationToken cancellationToken)
    {
        await conversations.GetOwnedOrThrowAsync(userId, conversationId, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        await db.SharedLinks.Where(s => s.ConversationId == conversationId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.RevokedAt, now), cancellationToken);
    }

    public async Task<SharedConversationDto?> GetSharedAsync(string token, CancellationToken cancellationToken)
    {
        // Wrong, revoked and deleted links look the same: nothing tells a guesser which tokens once existed.
        if (HashOrNull(token) is not { } hash)
        {
            return null;
        }
        var link = await db.SharedLinks.AsNoTracking()
            .Where(s => s.TokenHash == hash && s.RevokedAt == null)
            .Select(s => new { s.Title, s.CreatedAt, s.SnapshotJson })
            .FirstOrDefaultAsync(cancellationToken);
        if (link is null)
        {
            return null;
        }
        var snapshot = JsonSerializer.Deserialize<List<SharedMessageDto>>(link.SnapshotJson, SnapshotJson) ?? [];
        return new SharedConversationDto(link.Title, link.CreatedAt, snapshot);
    }

    /// <summary>The visible thread (active versions only), user and AI messages, citations as readable text.</summary>
    private async Task<List<SharedMessageDto>> SnapshotAsync(int conversationId, CancellationToken cancellationToken)
    {
        var nodes = await messages.GetNodesAsync(conversationId, cancellationToken);
        var thread = ConversationTree.ActiveThread(nodes).Select(t => t.Node.MessageId).ToList();
        var all = await messages.GetByIdsAsync(thread, cancellationToken);
        return all
            .Where(m => m.Role is MessageRole.User or MessageRole.Assistant)
            .Select(m => new SharedMessageDto(
                m.Role == MessageRole.User ? "user" : "assistant",
                // "[file:123]" markers point at private passages: show "(memo.pdf, Page 2)" instead.
                m.Role == MessageRole.Assistant ? Files.Citations.Readable(m) : m.Content,
                m.CreatedAt,
                m.Attachments.Where(a => a.File is not null).Select(a => a.File.OriginalFileName).ToList(),
                m.IsTruncated))
            .ToList();
    }

    /// <summary>SHA-256 of a well-formed token (43 base64url characters = 32 bytes), else null.</summary>
    internal static byte[]? HashOrNull(string? token)
    {
        if (token is null || token.Length != 43)
        {
            return null;
        }
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(token);
            return bytes.Length == TokenBytes ? SHA256.HashData(bytes) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
