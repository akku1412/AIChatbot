using System.Globalization;
using System.Security.Cryptography;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace ChatGPTClone.Api.Authentication;

/// <summary>
/// Issues the short-lived challenge token that ties the OTP endpoints to the Google-authenticated user,
/// so the client never says "which email" itself. Uses ASP.NET Core Data Protection (authenticated
/// encryption) with a dedicated purpose string, so it cannot be confused with any other token.
/// </summary>
public sealed class OtpChallengeProtector(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<OtpOptions> options) : IOtpChallengeProtector
{
    private const string Purpose = "ChatGPTClone.OtpChallenge.v1";

    private readonly ITimeLimitedDataProtector _protector =
        dataProtectionProvider.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    public string Create(int userId) =>
        _protector.Protect(
            userId.ToString(CultureInfo.InvariantCulture),
            TimeSpan.FromMinutes(options.Value.ChallengeLifetimeMinutes));

    public int? TryRead(string challengeToken)
    {
        try
        {
            var payload = _protector.Unprotect(challengeToken, out _);
            return int.TryParse(payload, NumberStyles.None, CultureInfo.InvariantCulture, out var userId) ? userId : null;
        }
        catch (CryptographicException)
        {
            // Tampered, forged, expired, or not base64url at all.
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
