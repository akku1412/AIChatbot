using System.ClientModel;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Models;
using OpenAI;
using OpenAI.Chat;

// reasoning_effort is a standard Chat Completions parameter (OpenAI and Groq), but the SDK still marks
// its .NET type as "evaluation" (OPENAI001). It is only sent when ReasoningEffort is configured.
#pragma warning disable OPENAI001

namespace ChatGPTClone.Api.Services.Ai;

/// <summary>
/// Chat completions through the official OpenAI .NET SDK. Works with OpenAI itself and with any
/// OpenAI-compatible API (Groq, Azure OpenAI, ...) via a custom endpoint. Keys never leave the server.
/// </summary>
public sealed class OpenAiChatProvider : IAiChatProvider
{
    private readonly ChatClient? _client;
    private readonly ChatReasoningEffortLevel? _reasoningEffort;
    private readonly ILogger<OpenAiChatProvider> _logger;

    public OpenAiChatProvider(string name, OpenAiProviderOptions settings, int timeoutSeconds,
        ILogger<OpenAiChatProvider> logger)
    {
        Name = name;
        _logger = logger;
        if (!string.IsNullOrWhiteSpace(settings.ReasoningEffort))
        {
            _reasoningEffort = new ChatReasoningEffortLevel(settings.ReasoningEffort.Trim().ToLowerInvariant());
        }
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return; // Not configured: CompleteAsync reports it clearly instead of failing at startup.
        }

        var clientOptions = new OpenAIClientOptions { NetworkTimeout = TimeSpan.FromSeconds(timeoutSeconds) };
        if (!string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            clientOptions.Endpoint = new Uri(settings.Endpoint);
        }

        _client = new ChatClient(settings.Model, new ApiKeyCredential(settings.ApiKey), clientOptions);
    }

    public string Name { get; }

    public async Task<AiCompletion> CompleteAsync(IReadOnlyList<AiChatMessage> messages, int maxOutputTokens,
        CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            throw new AiProviderException(AiFailureKind.NotConfigured, $"{Name} API key is not configured.");
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
                new ChatCompletionOptions { MaxOutputTokenCount = maxOutputTokens, ReasoningEffortLevel = _reasoningEffort },
                cancellationToken);
        }
        catch (ClientResultException ex)
        {
            throw Translate(ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiProviderException(AiFailureKind.Timeout, $"{Name} did not respond in time.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new AiProviderException(AiFailureKind.Unavailable, $"Could not reach {Name}.", ex);
        }

        if (completion.FinishReason == ChatFinishReason.ContentFilter)
        {
            throw new AiProviderException(AiFailureKind.ContentRejected, $"{Name} content filter blocked the reply.");
        }

        var text = string.Concat(completion.Content.Where(p => p.Kind == ChatMessageContentPartKind.Text).Select(p => p.Text));
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new AiProviderException(AiFailureKind.EmptyResponse, $"{Name} returned an empty reply.");
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
        _logger.LogWarning("{Provider} request failed with HTTP {Status} ({Kind})", Name, ex.Status, kind);
        return new AiProviderException(kind, $"{Name} request failed with HTTP {ex.Status}.", ex);
    }
}
