namespace ChatGPTClone.Api.Configuration;

public static class RateLimitPolicies
{
    /// <summary>Per-IP limit for the unauthenticated auth endpoints (Google sign-in, OTP send/verify).</summary>
    public const string Auth = "auth";

    /// <summary>Per-user limit on AI messages, which cost money.</summary>
    public const string AiChat = "ai-chat";

    /// <summary>Per-user limit on file uploads (each one is stored and parsed).</summary>
    public const string Uploads = "uploads";
}
