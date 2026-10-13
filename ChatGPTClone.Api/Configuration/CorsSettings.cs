namespace ChatGPTClone.Api.Configuration;

/// <summary>Bound from the "Cors" section. Lists the browser origins allowed to call the API.</summary>
public sealed class CorsSettings
{
    public const string SectionName = "Cors";
    public const string PolicyName = "Frontend";

    public string[] AllowedOrigins { get; init; } = [];
}
