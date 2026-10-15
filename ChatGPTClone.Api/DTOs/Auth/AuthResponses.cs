using ChatGPTClone.Api.DTOs.Users;

namespace ChatGPTClone.Api.DTOs.Auth;

/// <summary>
/// Returned by send-otp / resend-otp. Identical for new and existing accounts (no account enumeration).
/// CodeSent is false when a code sent moments ago is still valid and was reused instead.
/// </summary>
public sealed record OtpSentResponse(
    string Email,
    bool CodeSent,
    int ExpiresInSeconds,
    int ResendAvailableInSeconds,
    string Message);

/// <summary>
/// Returned by verify-otp, google and refresh. The refresh token is NOT in the body: it is set as an
/// HttpOnly cookie. Keep the access token in memory only (never localStorage).
/// </summary>
public sealed record AuthResponse(string AccessToken, DateTime AccessTokenExpiresAt, UserDto User);
