namespace ChatGPTClone.Api.DTOs.Auth;

/// <summary>The "credential" string Google Identity Services returns to the browser.</summary>
public sealed record GoogleLoginRequest(string IdToken);

/// <summary>Requests a fresh code for the pending verification identified by the challenge token.</summary>
public sealed record SendOtpRequest(string ChallengeToken);

public sealed record VerifyOtpRequest(string ChallengeToken, string Code);
