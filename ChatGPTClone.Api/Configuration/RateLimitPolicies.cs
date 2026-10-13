namespace ChatGPTClone.Api.Configuration;

public static class RateLimitPolicies
{
    /// <summary>Per-IP limit for the unauthenticated auth endpoints (Google sign-in, OTP send/verify).</summary>
    public const string Auth = "auth";
}
