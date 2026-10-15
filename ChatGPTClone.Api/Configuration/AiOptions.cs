using System.ComponentModel.DataAnnotations;

namespace ChatGPTClone.Api.Configuration;

/// <summary>
/// Bound from "Ai". Provider picks the IAiChatProvider implementation, so another model/vendor can be
/// added later by writing one class and changing configuration. API keys come from user secrets / env vars.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Currently supported: "OpenAI".</summary>
    [Required]
    public string Provider { get; init; } = "OpenAI";

    [Required]
    public string SystemPrompt { get; init; } =
        "You are AIChat, a helpful, accurate and concise assistant. Format answers with Markdown when it helps " +
        "(lists, tables, fenced code blocks with a language). If you are not sure about something, say so.";

    /// <summary>How many earlier messages of the conversation are sent as context.</summary>
    [Range(2, 100)]
    public int MaxHistoryMessages { get; init; } = 20;

    /// <summary>Upper bound on context size (characters) so long chats don't explode token usage.</summary>
    [Range(1000, 400_000)]
    public int MaxContextCharacters { get; init; } = 48_000;

    [Range(64, 32_000)]
    public int MaxOutputTokens { get; init; } = 2048;

    [Range(5, 300)]
    public int TimeoutSeconds { get; init; } = 90;

    public OpenAiProviderOptions OpenAI { get; init; } = new();

    /// <summary>Groq's OpenAI-compatible API (free tier available).</summary>
    public OpenAiProviderOptions Groq { get; init; } = new();

    /// <summary>Settings of the provider named by <see cref="Provider"/>.</summary>
    public OpenAiProviderOptions Active => Provider switch
    {
        "Groq" => Groq,
        _ => OpenAI,
    };
}

/// <summary>Settings for any OpenAI-compatible chat completions API.</summary>
public sealed class OpenAiProviderOptions
{
    /// <summary>Secret. Empty means "not configured": the app still runs and chat returns a clear 503.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    /// <summary>Base URL of an OpenAI-compatible API. Empty = api.openai.com.</summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// For reasoning models only ("low", "medium", "high"). Without it, short outputs such as titles can spend
    /// their whole token budget on hidden reasoning and come back empty. Leave empty for other models.
    /// </summary>
    public string? ReasoningEffort { get; set; }
}
