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
        "(lists, tables, fenced code blocks with a language). If you are not sure about something, say so.\n\n" +
        "Coding help (C#, ASP.NET Core, Angular, TypeScript, SQL, Python and others):\n" +
        "- Always put code in fenced blocks tagged with the language (```csharp, ```typescript, ```sql, ```python, ```html, ```json, ```bash).\n" +
        "- When debugging, use these headings: **Problem** (the error and where it happens), **Cause**, **Fix** (corrected code), " +
        "and **Why it works**. Quote error messages exactly.\n" +
        "- When optimizing, show the improved code and list the changes and their trade-offs.\n" +
        "- You cannot run code: never claim you executed or tested it; say what the user should run to verify.";

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
    public OpenAiProviderOptions Active => ProviderSettings(Provider);

    public OpenAiProviderOptions ProviderSettings(string provider) => provider switch
    {
        "Groq" => Groq,
        _ => OpenAI,
    };

    /// <summary>
    /// The models users may pick per chat (allow-list). Empty = one model: <see cref="Provider"/> with its Model.
    /// Clients only ever send an <see cref="AiModelOption.Id"/>; raw model names never come from the client.
    /// </summary>
    public List<AiModelOption> Models { get; init; } = [];

    /// <summary>Id from <see cref="Models"/> for new chats and chats whose model was removed. Empty = the first one.</summary>
    public string? DefaultModel { get; init; }
}

/// <summary>One entry of the model picker, e.g. "Fast" (small, cheap) or "Smart" (larger, slower).</summary>
public sealed class AiModelOption
{
    /// <summary>Stable id stored on conversations and messages, e.g. "fast". Never rename a published id.</summary>
    [Required, RegularExpression("^[a-z0-9][a-z0-9-]{0,31}$")]
    public string Id { get; init; } = string.Empty;

    [Required, StringLength(40)]
    public string Label { get; init; } = string.Empty;

    /// <summary>One line shown under the label in the picker.</summary>
    [StringLength(120)]
    public string? Description { get; init; }

    /// <summary>"OpenAI" or "Groq": whose API key and endpoint to use.</summary>
    [Required]
    public string Provider { get; init; } = "OpenAI";

    /// <summary>The provider's model name.</summary>
    [Required]
    public string Model { get; init; } = string.Empty;

    /// <summary>For reasoning models ("low", "medium", "high").</summary>
    public string? ReasoningEffort { get; init; }

    /// <summary>The model itself accepts images; otherwise image questions go to the provider's VisionModel.</summary>
    public bool SupportsVision { get; init; }
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

    /// <summary>
    /// A model that accepts images, used automatically when a message has images attached
    /// (e.g. "qwen/qwen3.8-27b" on Groq, "gpt-4.1-mini" on OpenAI). Empty = images can't be analysed.
    /// </summary>
    public string? VisionModel { get; set; }
}
