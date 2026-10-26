using ChatGPTClone.Api.DTOs.Auth;
using ChatGPTClone.Api.DTOs.Users;

namespace ChatGPTClone.Api.Interfaces;

public interface IEmailSender
{
    /// <summary>Sends one email. Throws EmailDeliveryException when the provider rejects or is unreachable.</summary>
    Task SendAsync(string toEmail, string toName, string subject, string htmlBody, string textBody,
        CancellationToken cancellationToken);
}

public interface IOtpService
{
    /// <summary>
    /// Issues a new code for the (lower-cased) email, invalidating older ones, and emails it.
    /// When <paramref name="explicitResend"/> is false and a recent code is still valid, no new email is sent.
    /// </summary>
    Task<OtpIssueResult> IssueAsync(string email, string? recipientName, bool explicitResend,
        CancellationToken cancellationToken);

    /// <summary>Checks a code. Throws ApiException with a user-facing reason when it is not accepted.</summary>
    Task VerifyAsync(string email, string code, CancellationToken cancellationToken);
}

public sealed record OtpIssueResult(bool CodeSent, int ExpiresInSeconds, int ResendAvailableInSeconds);

/// <summary>A completed sign-in: the response body plus the refresh token the controller puts in a cookie.</summary>
public sealed record AuthSession(AuthResponse Response, IssuedRefreshToken RefreshToken);

public interface IAuthService
{
    /// <summary>Email sign-in, step 1: emails a code. Same response whether or not an account exists.</summary>
    Task<OtpSentResponse> SendOtpAsync(string email, CancellationToken cancellationToken);

    /// <summary>Email sign-in: a new code after the cooldown (invalidates the previous one).</summary>
    Task<OtpSentResponse> ResendOtpAsync(string email, CancellationToken cancellationToken);

    /// <summary>Email sign-in, step 2: verifies the code, creates the account if new, and starts a session.</summary>
    Task<AuthSession> VerifyOtpAsync(string email, string code, CancellationToken cancellationToken);

    /// <summary>Google sign-in: validates the ID token server-side, finds/creates/links the account, starts a session.</summary>
    Task<AuthSession> SignInWithGoogleAsync(string idToken, CancellationToken cancellationToken);

    Task<AuthSession> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
    Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken);
}

public interface IUserService
{
    Task<UserDto> GetAsync(int userId, CancellationToken cancellationToken);
    Task<UserDto> UpdateProfileAsync(int userId, string fullName, CancellationToken cancellationToken);

    /// <summary>Custom instructions; empty when never saved.</summary>
    Task<UserSettingsDto> GetSettingsAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Saves the fields that are present: custom instructions (trimmed; blank = cleared) and the theme.</summary>
    Task<UserSettingsDto> UpdateSettingsAsync(int userId, UpdateUserSettingsRequest request, CancellationToken cancellationToken);
}
