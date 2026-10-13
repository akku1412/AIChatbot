using ChatGPTClone.Api.Models;

namespace ChatGPTClone.Api.Interfaces;

public sealed record AiChatMessage(MessageRole Role, string Content);

public sealed record AiCompletion(string Content, int? TotalTokens, bool Truncated);

/// <summary>Vendor-neutral chat completion. Implementations: OpenAiChatProvider (add more for other models).</summary>
public interface IAiChatProvider
{
    string Name { get; }

    /// <summary>Throws AiProviderException for every provider-side failure.</summary>
    Task<AiCompletion> CompleteAsync(IReadOnlyList<AiChatMessage> messages, int maxOutputTokens,
        CancellationToken cancellationToken);
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
}

public sealed class AiProviderException(AiFailureKind kind, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public AiFailureKind Kind { get; } = kind;
}
