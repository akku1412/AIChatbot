namespace ChatGPTClone.Api.Models;

public class OtpVerification : IHasCreatedAt
{
    public int OtpVerificationId { get; set; }

    public int UserId { get; set; }

    /// <summary>Keyed hash of the 6-digit code. The plaintext OTP is never stored.</summary>
    public string OtpHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    /// <summary>Set when the code is verified OR invalidated by a newer code; a used code can never be accepted again.</summary>
    public bool IsUsed { get; set; }

    /// <summary>Number of wrong guesses against this code.</summary>
    public int AttemptCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;
}
