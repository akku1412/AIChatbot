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

/// <summary>Phase 4 result. Phase 5 adds the access token (and refresh cookie) here.</summary>
public sealed record VerifyOtpResponse(bool Verified, UserDto User);
