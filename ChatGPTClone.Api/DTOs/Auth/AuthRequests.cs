namespace ChatGPTClone.Api.DTOs.Auth;

/// <summary>The "credential" string Google Identity Services returns to the browser.</summary>
public sealed record GoogleLoginRequest(string IdToken);

/// <summary>Body of POST /api/auth/send-otp and /api/auth/resend-otp.</summary>
public sealed record EmailOtpRequest(string Email);

public sealed record VerifyOtpRequest(string Email, string Code);
