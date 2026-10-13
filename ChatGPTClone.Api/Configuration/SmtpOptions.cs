using System.ComponentModel.DataAnnotations;

namespace ChatGPTClone.Api.Configuration;

/// <summary>
/// Bound from "Smtp". Host/Port/FromName live in appsettings.json;
/// Username/Password/FromEmail come from user secrets or environment variables.
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    [Required]
    public string Host { get; init; } = string.Empty;

    /// <summary>587 = STARTTLS, 465 = implicit TLS.</summary>
    [Range(1, 65535)]
    public int Port { get; init; } = 587;

    [Required]
    public string Username { get; init; } = string.Empty;

    /// <summary>For Gmail this is a 16-character App Password, never the account password.</summary>
    [Required]
    public string Password { get; init; } = string.Empty;

    [Required, EmailAddress]
    public string FromEmail { get; init; } = string.Empty;

    [Required]
    public string FromName { get; init; } = "AIChat";
}
