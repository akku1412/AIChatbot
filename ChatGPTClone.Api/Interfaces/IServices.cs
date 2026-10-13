using ChatGPTClone.Api.DTOs.Auth;
using ChatGPTClone.Api.DTOs.Users;
using ChatGPTClone.Api.Models;

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
    /// Issues a new code (invalidating older ones) and emails it.
    /// When <paramref name="explicitResend"/> is false and a recent code is still valid, no new email is sent.
    /// </summary>
    Task<OtpIssueResult> IssueAsync(User user, bool explicitResend, CancellationToken cancellationToken);

    /// <summary>Checks a code. Throws ApiException with a user-facing reason when it is not accepted.</summary>
    Task VerifyAsync(User user, string code, CancellationToken cancellationToken);
}

public sealed record OtpIssueResult(bool CodeSent, int ExpiresInSeconds, int ResendAvailableInSeconds);

/// <summary>A completed sign-in: the response body plus the refresh token the controller puts in a cookie.</summary>
public sealed record AuthSession(AuthResponse Response, IssuedRefreshToken RefreshToken);

public interface IAuthService
{
    Task<OtpChallengeResponse> SignInWithGoogleAsync(string idToken, CancellationToken cancellationToken);
    Task<OtpChallengeResponse> ResendOtpAsync(string challengeToken, CancellationToken cancellationToken);

    /// <summary>The ONLY place a new session (access + refresh token) is created.</summary>
    Task<AuthSession> VerifyOtpAsync(string challengeToken, string code, CancellationToken cancellationToken);

    Task<AuthSession> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
    Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken);
}

public interface IUserService
{
    Task<UserDto> GetAsync(int userId, CancellationToken cancellationToken);
    Task<UserDto> UpdateProfileAsync(int userId, string fullName, CancellationToken cancellationToken);
}
