using System.Security.Cryptography;
using System.Text;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Interfaces;
using Microsoft.Extensions.Options;

namespace ChatGPTClone.Api.Authentication;

/// <summary>
/// Stores OTPs as "base64(salt).hex(HMAC-SHA256(key, salt || email:code))".
/// A 6-digit code has only a million values, so a plain hash could be reversed instantly;
/// the secret HMAC key (from configuration, not the database) is what makes a leaked hash useless.
/// </summary>
public sealed class OtpHasher : IOtpHasher
{
    private const int SaltSize = 16;
    private readonly byte[] _key;

    public OtpHasher(IOptions<OtpOptions> options)
    {
        var configured = options.Value.HashKey;
        try
        {
            _key = Convert.FromBase64String(configured);
        }
        catch (FormatException)
        {
            _key = Encoding.UTF8.GetBytes(configured);
        }
    }

    public string Hash(string email, string code)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToHexString(Compute(salt, email, code))}";
    }

    public bool Verify(string email, string code, string storedHash)
    {
        var parts = storedHash.Split('.');
        if (parts.Length != 2)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[0]);
            var expected = Convert.FromHexString(parts[1]);
            return CryptographicOperations.FixedTimeEquals(Compute(salt, email, code), expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private byte[] Compute(byte[] salt, string email, string code)
    {
        var message = Encoding.UTF8.GetBytes($"{email}:{code}");
        var input = new byte[salt.Length + message.Length];
        salt.CopyTo(input, 0);
        message.CopyTo(input, salt.Length);
        return HMACSHA256.HashData(_key, input);
    }
}
