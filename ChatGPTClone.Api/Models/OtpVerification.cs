namespace ChatGPTClone.Api.Models;

/// <summary>
/// One emailed sign-in code. Keyed by email (not user) because email sign-in creates the account only
/// after the code is verified.
/// </summary>
public class OtpVerification : IHasCreatedAt
{
    public int OtpVerificationId { get; set; }

    /// <summary>Lower-cased address the code was sent to.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Keyed hash of the 6-digit code. The plaintext OTP is never stored.</summary>
    public string OtpHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    /// <summary>Set when the code is verified OR invalidated by a newer code; a used code can never be accepted again.</summary>
    public bool IsUsed { get; set; }

    /// <summary>Number of wrong guesses against this code.</summary>
    public int AttemptCount { get; set; }

    public DateTime CreatedAt { get; set; }
}
