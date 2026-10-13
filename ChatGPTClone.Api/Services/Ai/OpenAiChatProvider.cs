using System.ClientModel;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Models;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace ChatGPTClone.Api.Services.Ai;

/// <summary>OpenAI Chat Completions via the official OpenAI .NET SDK. The key never leaves the server.</summary>
public sealed class OpenAiChatProvider : IAiChatProvider
{
    private readonly ChatClient? _client;
    private readonly ILogger<OpenAiChatProvider> _logger;

    public OpenAiChatProvider(IOptions<AiOptions> options, ILogger<OpenAiChatProvider> logger)
    {
        _logger = logger;
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.OpenAI.ApiKey))
        {
            return; // Not configured: CompleteAsync reports it clearly instead of failing at startup.
        }

        var clientOptions = new OpenAIClientOptions { NetworkTimeout = TimeSpan.FromSeconds(settings.TimeoutSeconds) };
        if (!string.IsNullOrWhiteSpace(settings.OpenAI.Endpoint))
        {
            clientOptions.Endpoint = new Uri(settings.OpenAI.Endpoint);
        }

        _client = new ChatClient(settings.OpenAI.Model, new ApiKeyCredential(settings.OpenAI.ApiKey), clientOptions);
    }

    public string Name => "OpenAI";

    public async Task<AiCompletion> CompleteAsync(IReadOnlyList<AiChatMessage> messages, int maxOutputTokens,
        CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            throw new AiProviderException(AiFailureKind.NotConfigured, "OpenAI API key is not configured.");
        }

        var chatMessages = messages.Select<AiChatMessage, ChatMessage>(m => m.Role switch
        {
            MessageRole.System => new SystemChatMessage(m.Content),
            MessageRole.Assistant => new AssistantChatMessage(m.Content),
            _ => new UserChatMessage(m.Content),
        }).ToList();

        ChatCompletion completion;
        try
        {
            completion = await _client.CompleteChatAsync(
                chatMessages,
                new ChatCompletionOptions { MaxOutputTokenCount = maxOutputTokens },
                cancellationToken);
        }
        catch (ClientResultException ex)
        {
            throw Translate(ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiProviderException(AiFailureKind.Timeout, "OpenAI did not respond in time.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new AiProviderException(AiFailureKind.Unavailable, "Could not reach OpenAI.", ex);
        }

        if (completion.FinishReason == ChatFinishReason.ContentFilter)
        {
            throw new AiProviderException(AiFailureKind.ContentRejected, "OpenAI content filter blocked the reply.");
        }

        var text = string.Concat(completion.Content.Where(p => p.Kind == ChatMessageContentPartKind.Text).Select(p => p.Text));
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new AiProviderException(AiFailureKind.EmptyResponse, "OpenAI returned an empty reply.");
        }

        return new AiCompletion(text.Trim(), completion.Usage?.TotalTokenCount,
            completion.FinishReason == ChatFinishReason.Length);
    }

    private AiProviderException Translate(ClientResultException ex)
    {
        // The exception message can echo request details; log only status and kind.
        var kind = ex.Status switch
        {
            401 or 403 => AiFailureKind.InvalidCredentials,
            429 when ex.Message.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase) => AiFailureKind.QuotaExceeded,
            429 => AiFailureKind.RateLimited,
            400 when ex.Message.Contains("content", StringComparison.OrdinalIgnoreCase)
                  && ex.Message.Contains("policy", StringComparison.OrdinalIgnoreCase) => AiFailureKind.ContentRejected,
            _ => AiFailureKind.Unavailable,
        };
        _logger.LogWarning("OpenAI request failed with HTTP {Status} ({Kind})", ex.Status, kind);
        return new AiProviderException(kind, $"OpenAI request failed with HTTP {ex.Status}.", ex);
    }
}
