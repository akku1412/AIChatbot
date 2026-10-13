using ChatGPTClone.Api.DTOs.Users;

namespace ChatGPTClone.Api.DTOs.Auth;

/// <summary>
/// Returned after Google sign-in and after a resend. The challenge token only authorizes the OTP
/// endpoints for this user; it is NOT an application access token.
/// </summary>
public sealed record OtpChallengeResponse(
    string ChallengeToken,
    string MaskedEmail,
    bool CodeSent,
    int ExpiresInSeconds,
    int ResendAvailableInSeconds);

/// <summary>
/// Returned by verify-otp and refresh. The refresh token is NOT in the body: it is set as an HttpOnly cookie.
/// Keep the access token in memory only (never localStorage).
/// </summary>
public sealed record AuthResponse(string AccessToken, DateTime AccessTokenExpiresAt, UserDto User);
