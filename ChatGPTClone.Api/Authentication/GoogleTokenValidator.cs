using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Interfaces;
using Google.Apis.Auth;
using Microsoft.Extensions.Options;

namespace ChatGPTClone.Api.Authentication;

public sealed class GoogleTokenValidator(
    IOptions<GoogleAuthOptions> options,
    ILogger<GoogleTokenValidator> logger) : IGoogleTokenValidator
{
    public async Task<GoogleIdentity?> ValidateAsync(string idToken)
    {
        GoogleJsonWebSignature.Payload payload;
        try
        {
            // Checks the RS256 signature against Google's published keys, the issuer
            // (accounts.google.com), expiry, and that the audience is OUR client ID.
            payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [options.Value.ClientId],
            });
        }
        catch (InvalidJwtException ex)
        {
            // ex.Message describes the failed check (e.g. "JWT has expired"); it never contains the token.
            logger.LogWarning("Rejected Google ID token: {Reason}", ex.Message);
            return null;
        }

        if (!payload.EmailVerified || string.IsNullOrWhiteSpace(payload.Email) || string.IsNullOrWhiteSpace(payload.Subject))
        {
            logger.LogWarning("Rejected Google ID token for subject {GoogleId}: email missing or not verified by Google",
                payload.Subject);
            return null;
        }

        var email = payload.Email.Trim().ToLowerInvariant();
        var fullName = string.IsNullOrWhiteSpace(payload.Name) ? email : payload.Name.Trim();

        return new GoogleIdentity(payload.Subject, email, fullName, payload.Picture);
    }
}
