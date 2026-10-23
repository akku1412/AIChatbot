using System.Runtime.CompilerServices;
using ChatGPTClone.Api.Models;

namespace ChatGPTClone.Api.Interfaces;

public sealed record AiChatMessage(MessageRole Role, string Content);

public sealed record AiCompletion(string Content, int? TotalTokens, bool Truncated);

/// <summary>A piece of a streamed reply. The final update has empty text and carries usage and truncation.</summary>
public sealed record AiStreamUpdate(string Text, int? TotalTokens = null, bool Truncated = false);

/// <summary>Vendor-neutral chat completion. Implementations: OpenAiChatProvider (add more for other models).</summary>
public interface IAiChatProvider
{
    string Name { get; }

    /// <summary>Throws AiProviderException for every provider-side failure.</summary>
    Task<AiCompletion> CompleteAsync(IReadOnlyList<AiChatMessage> messages, int maxOutputTokens,
        CancellationToken cancellationToken);

    /// <summary>
    /// The reply as it is generated. Throws AiProviderException like CompleteAsync (also part-way through).
    /// Providers without streaming get this default: the whole reply as one piece.
    /// </summary>
    IAsyncEnumerable<AiStreamUpdate> StreamAsync(IReadOnlyList<AiChatMessage> messages, int maxOutputTokens,
        CancellationToken cancellationToken) => AiStreaming.FromCompletion(this, messages, maxOutputTokens, cancellationToken);
}

public static class AiStreaming
{
    public static async IAsyncEnumerable<AiStreamUpdate> FromCompletion(IAiChatProvider provider,
        IReadOnlyList<AiChatMessage> messages, int maxOutputTokens, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var completion = await provider.CompleteAsync(messages, maxOutputTokens, cancellationToken);
        yield return new AiStreamUpdate(completion.Content);
        yield return new AiStreamUpdate("", completion.TotalTokens, completion.Truncated);
    }
}

public enum AiFailureKind
{
    NotConfigured,
    InvalidCredentials,
    QuotaExceeded,
    RateLimited,
    Timeout,
    ContentRejected,
    Unavailable,
    EmptyResponse,
    /// <summary>The prompt (history + documents) exceeded the model's context window.</summary>
    ContextTooLong,
}

public sealed class AiProviderException(AiFailureKind kind, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public AiFailureKind Kind { get; } = kind;
}
