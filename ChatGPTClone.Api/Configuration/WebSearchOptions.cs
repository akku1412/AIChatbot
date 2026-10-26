using System.ComponentModel.DataAnnotations;

namespace ChatGPTClone.Api.Configuration;

/// <summary>
/// Bound from "WebSearch": the "Search the web" toggle. The API key is a secret (user secrets / environment variable
/// WebSearch__ApiKey); without it the toggle is hidden and requests asking for web search are answered without it.
/// </summary>
public sealed class WebSearchOptions
{
    public const string SectionName = "WebSearch";

    /// <summary>Currently supported: "Tavily".</summary>
    [Required]
    public string Provider { get; init; } = "Tavily";

    /// <summary>Secret. Empty = web search is off.</summary>
    public string? ApiKey { get; init; }

    [Required, Url]
    public string Endpoint { get; init; } = "https://api.tavily.com/search";

    /// <summary>"basic" (fast, cheaper) or "advanced".</summary>
    public string SearchDepth { get; init; } = "basic";

    [Range(1, 10)]
    public int MaxResults { get; init; } = 5;

    /// <summary>Each result's text is cut to this many characters before it reaches the prompt.</summary>
    [Range(200, 5000)]
    public int MaxCharactersPerResult { get; init; } = 1500;

    [Range(3, 60)]
    public int TimeoutSeconds { get; init; } = 15;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
