using System.Security.Cryptography;
using ChatGPTClone.Api.Authentication;
using ChatGPTClone.Api.Configuration;
using Microsoft.Extensions.Options;

namespace ChatGPTClone.Api.Tests.Unit;

public class OtpHasherTests
{
    private static OtpHasher CreateHasher(string? key = null) =>
        new(Options.Create(new OtpOptions { HashKey = key ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));

    [Fact]
    public void Verify_accepts_the_code_that_was_hashed()
    {
        var hasher = CreateHasher();
        var hash = hasher.Hash("user@example.test", "123456");

        Assert.True(hasher.Verify("user@example.test", "123456", hash));
    }

    [Theory]
    [InlineData("user@example.test", "123457")] // wrong code
    [InlineData("other@example.test", "123456")] // right code, different email
    public void Verify_rejects_wrong_code_or_email(string email, string code)
    {
        var hasher = CreateHasher();
        var hash = hasher.Hash("user@example.test", "123456");

        Assert.False(hasher.Verify(email, code, hash));
    }

    [Fact]
    public void Hash_never_contains_the_plain_code_and_is_salted()
    {
        var hasher = CreateHasher();
        var first = hasher.Hash("user@example.test", "123456");
        var second = hasher.Hash("user@example.test", "123456");

        Assert.DoesNotContain("123456", first);
        Assert.NotEqual(first, second); // random salt per code
        Assert.True(first.Length <= 128); // fits OtpVerifications.OtpHash
    }

    [Fact]
    public void A_different_server_key_cannot_verify_the_hash()
    {
        var hash = CreateHasher().Hash("user@example.test", "123456");

        Assert.False(CreateHasher().Verify("user@example.test", "123456", hash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("abc.def.ghi")]
    [InlineData("%%%.zz")]
    public void Verify_returns_false_for_malformed_stored_values(string stored)
    {
        Assert.False(CreateHasher().Verify("user@example.test", "123456", stored));
    }
}
