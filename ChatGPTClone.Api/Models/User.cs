namespace ChatGPTClone.Api.Models;

public class User : IAuditable
{
    public int UserId { get; set; }

    /// <summary>
    /// Google account identifier (the "sub" claim), i.e. the Google subject id. Null for accounts created
    /// with email OTP until the user signs in with Google using the same email (then it is linked).
    /// </summary>
    public string? GoogleId { get; set; }

    public string FullName { get; set; } = string.Empty;

    /// <summary>Stored lower-cased; unique.</summary>
    public string Email { get; set; } = string.Empty;

    public string? ProfileImageUrl { get; set; }

    /// <summary>True once the address was proven: an email OTP was verified, or Google reported it verified.</summary>
    public bool IsEmailVerified { get; set; }

    /// <summary><see cref="UserRoles.User"/> or <see cref="UserRoles.Admin"/>. Checked live on every request.</summary>
    public string Role { get; set; } = UserRoles.User;

    /// <summary>Blocked by an admin: can't sign in, and signed-in sessions get 403 on every protected endpoint.</summary>
    public bool IsBlocked { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
