using System.ClientModel;
using System.Runtime.CompilerServices;
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
        if (!string.IsNullOrWhiteSpace(settings.VisionModel))
        {
            _visionClient = new ChatClient(settings.VisionModel, new ApiKeyCredential(settings.ApiKey), clientOptions);
        }
    }

    /// <summary>Separate model for prompts with images (the main model may not accept them). Null = images are dropped.</summary>
    private readonly ChatClient? _visionClient;

    public string Name { get; }

    public async Task<AiCompletion> CompleteAsync(IReadOnlyList<AiChatMessage> messages, int maxOutputTokens,
        CancellationToken cancellationToken)
    {
        var (client, chatMessages, effort) = Prepare(messages);

        ChatCompletion completion;
        try
        {
            completion = await client.CompleteChatAsync(
                chatMessages,
                new ChatCompletionOptions { MaxOutputTokenCount = maxOutputTokens, ReasoningEffortLevel = effort },
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

    /// <summary>
    /// Streams the reply token by token (server-sent chunks from the provider). Failures are translated exactly
    /// like CompleteAsync, including ones that happen part-way through. Cancelling stops the provider request.
    /// </summary>
    public async IAsyncEnumerable<AiStreamUpdate> StreamAsync(IReadOnlyList<AiChatMessage> messages, int maxOutputTokens,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (client, chatMessages, effort) = Prepare(messages);
        var options = new ChatCompletionOptions { MaxOutputTokenCount = maxOutputTokens, ReasoningEffortLevel = effort };
        var updates = client.CompleteChatStreamingAsync(chatMessages, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        int? totalTokens = null;
        var truncated = false;
        var anyText = false;
        try
        {
            while (true)
            {
                StreamingChatCompletionUpdate update;
                try
                {
                    if (!await updates.MoveNextAsync())
                    {
                        break;
                    }
                    update = updates.Current;
                }
                catch (ClientResultException ex)
                {
                    throw Translate(ex);
                }
                catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new AiProviderException(AiFailureKind.Timeout, $"{Name} did not respond in time.", ex);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException)
                {
                    throw new AiProviderException(AiFailureKind.Unavailable, $"Lost the connection to {Name}.", ex);
                }

                if (update.FinishReason == ChatFinishReason.ContentFilter)
                {
                    throw new AiProviderException(AiFailureKind.ContentRejected, $"{Name} content filter blocked the reply.");
                }
                truncated |= update.FinishReason == ChatFinishReason.Length;
                totalTokens = update.Usage?.TotalTokenCount ?? totalTokens;

                var text = string.Concat(update.ContentUpdate.Where(p => p.Kind == ChatMessageContentPartKind.Text).Select(p => p.Text));
                if (text.Length > 0)
                {
                    anyText = true;
                    yield return new AiStreamUpdate(text);
                }
            }
        }
        finally
        {
            await updates.DisposeAsync();
        }

        if (!anyText)
        {
            throw new AiProviderException(AiFailureKind.EmptyResponse, $"{Name} returned an empty reply.");
        }
        yield return new AiStreamUpdate("", totalTokens, truncated);
    }

    private ChatClient RequireClient() =>
        _client ?? throw new AiProviderException(AiFailureKind.NotConfigured, $"{Name} API key is not configured.");

    /// <summary>
    /// Prompts with images go to the vision model (without reasoning_effort, which vision models may reject).
    /// Without a vision model the images are left out; the AI still has the text read from them (OCR).
    /// </summary>
    private (ChatClient Client, List<ChatMessage> Messages, ChatReasoningEffortLevel? Effort) Prepare(IReadOnlyList<AiChatMessage> messages)
    {
        var client = RequireClient();
        var hasImages = messages.Any(m => m.Images is { Count: > 0 });
        if (hasImages && _visionClient is not null)
        {
            return (_visionClient, ToChatMessages(messages, withImages: true), null);
        }
        if (hasImages)
        {
            _logger.LogInformation("{Provider} has no vision model configured; sending the prompt without images", Name);
        }
        return (client, ToChatMessages(messages, withImages: false), _reasoningEffort);
    }

    internal static List<ChatMessage> ToChatMessages(IReadOnlyList<AiChatMessage> messages, bool withImages) =>
        messages.Select<AiChatMessage, ChatMessage>(m => m.Role switch
        {
            MessageRole.System => new SystemChatMessage(m.Content),
            MessageRole.Assistant => new AssistantChatMessage(m.Content),
            _ when withImages && m.Images is { Count: > 0 } images => new UserChatMessage(
            [
                ChatMessageContentPart.CreateTextPart(m.Content),
                .. images.Select(i => ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(i.Data), i.ContentType)),
            ]),
            _ => new UserChatMessage(m.Content),
        }).ToList();

    private AiProviderException Translate(ClientResultException ex)
    {
        // The exception message can echo request details; log only status and kind.
        var kind = ex.Status switch
        {
            401 or 403 => AiFailureKind.InvalidCredentials,
            413 => AiFailureKind.ContextTooLong,
            400 when ex.Message.Contains("context_length", StringComparison.OrdinalIgnoreCase)
                  || ex.Message.Contains("maximum context", StringComparison.OrdinalIgnoreCase)
                  || ex.Message.Contains("too many tokens", StringComparison.OrdinalIgnoreCase) => AiFailureKind.ContextTooLong,
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
