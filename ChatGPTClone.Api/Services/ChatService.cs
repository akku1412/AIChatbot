using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.DTOs.Chat;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Middleware;
using ChatGPTClone.Api.Models;
using Microsoft.Extensions.Options;

namespace ChatGPTClone.Api.Services;

public interface IChatService
{
    Task<MessagePageDto> GetMessagesAsync(int userId, int conversationId, long? beforeMessageId, int limit,
        CancellationToken cancellationToken);

    Task<SendMessageResponse> SendAsync(int userId, int conversationId, string content, CancellationToken cancellationToken);
}

public sealed class ChatService(
    IConversationService conversations,
    IConversationRepository conversationRepository,
    IMessageRepository messageRepository,
    IAiChatProvider aiProvider,
    IOptions<AiOptions> options,
    ILogger<ChatService> logger) : IChatService
{
    private readonly AiOptions _options = options.Value;

    public async Task<MessagePageDto> GetMessagesAsync(int userId, int conversationId, long? beforeMessageId, int limit,
        CancellationToken cancellationToken)
    {
        await conversations.GetOwnedOrThrowAsync(userId, conversationId, cancellationToken);
        var (items, hasMore) = await messageRepository.GetPageAsync(conversationId, beforeMessageId, limit, cancellationToken);
        return new MessagePageDto(items.Select(MessageDto.From).ToList(), hasMore);
    }

    public async Task<SendMessageResponse> SendAsync(int userId, int conversationId, string content,
        CancellationToken cancellationToken)
    {
        var conversation = await conversations.GetOwnedOrThrowAsync(userId, conversationId, cancellationToken);
        var text = content.Trim();

        var history = await messageRepository.GetRecentAsync(conversationId, _options.MaxHistoryMessages, cancellationToken);
        var isFirstExchange = history.Count == 0;
        var prompt = BuildPrompt(history, text);

        // Ask the AI first: if it fails, nothing is saved, so the user can simply resend.
        var completion = await CompleteAsync(prompt, _options.MaxOutputTokens, cancellationToken);

        var userMessage = new Message { ConversationId = conversationId, Role = MessageRole.User, Content = text };
        messageRepository.Add(userMessage);
        await messageRepository.SaveChangesAsync(cancellationToken);

        // Saved separately so the assistant reply always gets the higher MessageId (message order = id order).
        var assistantMessage = new Message
        {
            ConversationId = conversationId,
            Role = MessageRole.Assistant,
            Content = completion.Content,
            TokenUsage = completion.TotalTokens,
        };
        messageRepository.Add(assistantMessage);

        if (isFirstExchange && conversation.Title == ConversationService.DefaultTitle)
        {
            conversation.Title = await GenerateTitleAsync(text, completion.Content, cancellationToken);
        }
        conversation.UpdatedAt = DateTime.UtcNow; // marks it modified; AppDbContext stamps the exact time
        await conversationRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Conversation {ConversationId}: reply from {Provider}, {Tokens} tokens{Truncated}",
            conversationId, aiProvider.Name, completion.TotalTokens, completion.Truncated ? " (truncated)" : "");

        return new SendMessageResponse(
            MessageDto.From(userMessage), MessageDto.From(assistantMessage), ConversationDto.From(conversation));
    }

    /// <summary>System prompt + as much recent history as fits the character budget + the new message.</summary>
    private List<AiChatMessage> BuildPrompt(List<Message> history, string newMessage)
    {
        var budget = _options.MaxContextCharacters - _options.SystemPrompt.Length - newMessage.Length;
        var kept = new List<AiChatMessage>();
        for (var i = history.Count - 1; i >= 0 && budget > 0; i--)
        {
            budget -= history[i].Content.Length;
            if (budget >= 0)
            {
                kept.Add(new AiChatMessage(history[i].Role, history[i].Content));
            }
        }
        kept.Reverse();

        return
        [
            new AiChatMessage(MessageRole.System, _options.SystemPrompt),
            .. kept,
            new AiChatMessage(MessageRole.User, newMessage),
        ];
    }

    private async Task<AiCompletion> CompleteAsync(List<AiChatMessage> prompt, int maxTokens, CancellationToken cancellationToken)
    {
        try
        {
            return await aiProvider.CompleteAsync(prompt, maxTokens, cancellationToken);
        }
        catch (AiProviderException ex)
        {
            logger.LogWarning("AI provider {Provider} failed: {Kind}", aiProvider.Name, ex.Kind);
            throw ToApiException(ex.Kind);
        }
    }

    /// <summary>A short title from the AI; falls back to the start of the first message if that fails.</summary>
    private async Task<string> GenerateTitleAsync(string userText, string assistantText, CancellationToken cancellationToken)
    {
        try
        {
            var result = await aiProvider.CompleteAsync(
            [
                new AiChatMessage(MessageRole.System,
                    "Write a short title (3 to 6 words) for this conversation. Reply with the title only: " +
                    "no quotes, no trailing punctuation, same language as the user."),
                new AiChatMessage(MessageRole.User, Truncate(userText, 1000)),
                new AiChatMessage(MessageRole.Assistant, Truncate(assistantText, 1000)),
                new AiChatMessage(MessageRole.User, "Title:"),
            ], 30, cancellationToken);

            var title = CleanTitle(result.Content);
            if (title.Length > 0)
            {
                return title;
            }
        }
        catch (AiProviderException ex)
        {
            logger.LogInformation("Title generation failed ({Kind}); using the first message instead", ex.Kind);
        }

        return FallbackTitle(userText);
    }

    internal static string CleanTitle(string raw)
    {
        var line = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        line = line.Trim().Trim('"', '\'', '“', '”', '*', '#', ' ').TrimEnd('.', '!', '?', ':');
        if (line.StartsWith("Title:", StringComparison.OrdinalIgnoreCase))
        {
            line = line["Title:".Length..].Trim();
        }
        return Truncate(line, 80);
    }

    internal static string FallbackTitle(string userText)
    {
        var flat = string.Join(' ', userText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (flat.Length <= 50)
        {
            return flat.Length == 0 ? ConversationService.DefaultTitle : flat;
        }
        var cut = flat[..50];
        var lastSpace = cut.LastIndexOf(' ');
        return (lastSpace > 20 ? cut[..lastSpace] : cut) + "…";
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static ApiException ToApiException(AiFailureKind kind) => kind switch
    {
        AiFailureKind.NotConfigured or AiFailureKind.InvalidCredentials => new ApiException(
            StatusCodes.Status503ServiceUnavailable, "AI service unavailable",
            "The AI service isn't configured correctly right now. Please try again later.",
            new Dictionary<string, object?> { ["reason"] = "ai_not_configured" }),
        AiFailureKind.QuotaExceeded => new ApiException(
            StatusCodes.Status503ServiceUnavailable, "AI service unavailable",
            "The AI service has reached its usage limit. Please try again later.",
            new Dictionary<string, object?> { ["reason"] = "ai_quota" }),
        AiFailureKind.RateLimited => new ApiException(
            StatusCodes.Status429TooManyRequests, "AI is busy",
            "The AI is receiving too many requests. Please wait a few seconds and try again.",
            new Dictionary<string, object?> { ["reason"] = "ai_rate_limited", ["retryAfterSeconds"] = 10 }),
        AiFailureKind.Timeout => new ApiException(
            StatusCodes.Status504GatewayTimeout, "AI took too long",
            "The AI took too long to respond. Please try again, perhaps with a shorter message.",
            new Dictionary<string, object?> { ["reason"] = "ai_timeout" }),
        AiFailureKind.ContentRejected => new ApiException(
            StatusCodes.Status422UnprocessableEntity, "Message not allowed",
            "The AI provider declined to respond to this message. Please rephrase it.",
            new Dictionary<string, object?> { ["reason"] = "ai_content_rejected" }),
        _ => new ApiException(
            StatusCodes.Status502BadGateway, "AI service error",
            "The AI service had a problem answering. Please try again.",
            new Dictionary<string, object?> { ["reason"] = "ai_error" }),
    };
}
