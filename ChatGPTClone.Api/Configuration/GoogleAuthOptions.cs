using System.ComponentModel.DataAnnotations;

namespace ChatGPTClone.Api.Configuration;

/// <summary>Bound from "GoogleAuth". The client ID is public (it is also in the Angular app).</summary>
public sealed class GoogleAuthOptions
{
    public const string SectionName = "GoogleAuth";

    /// <summary>OAuth Web client ID; Google ID tokens must have this as their audience.</summary>
    [Required]
    public string ClientId { get; init; } = string.Empty;
}
