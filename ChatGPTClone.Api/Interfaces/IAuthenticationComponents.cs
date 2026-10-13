using ChatGPTClone.Api.Authentication;

namespace ChatGPTClone.Api.Interfaces;

public interface IGoogleTokenValidator
{
    /// <summary>
    /// Verifies the Google ID token's signature, issuer, expiry and audience (our client ID) and that
    /// Google has verified the email. Returns null when the token must not be trusted.
    /// </summary>
    Task<GoogleIdentity?> ValidateAsync(string idToken);
}

public interface IOtpChallengeProtector
{
    /// <summary>Creates a signed, encrypted, time-limited token naming the user who is mid-verification.</summary>
    string Create(int userId);

    /// <summary>Returns the user id, or null if the token is forged, tampered with or expired.</summary>
    int? TryRead(string challengeToken);
}

public interface IOtpHasher
{
    string Hash(int userId, string code);
    bool Verify(int userId, string code, string storedHash);
}
