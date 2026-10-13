namespace ChatGPTClone.Api.Models;

public class User : IAuditable
{
    public int UserId { get; set; }

    /// <summary>Google account identifier (the "sub" claim). Never changes for a Google account.</summary>
    public string GoogleId { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    /// <summary>Stored lower-cased; unique.</summary>
    public string Email { get; set; } = string.Empty;

    public string? ProfileImageUrl { get; set; }

    /// <summary>True once the user has completed email OTP verification at least once.</summary>
    public bool IsEmailVerified { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<OtpVerification> OtpVerifications { get; set; } = new List<OtpVerification>();
    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
