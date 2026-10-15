using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static ChatGPTClone.Api.Tests.Integration.TestAuth;

namespace ChatGPTClone.Api.Tests.Integration;

[Collection(ApiCollection.Name)]
public class SecurityTests(ApiFactory factory)
{
    [Theory]
    [InlineData("GET", "/api/conversations")]
    [InlineData("POST", "/api/conversations")]
    [InlineData("GET", "/api/chat/1/messages")]
    [InlineData("POST", "/api/chat/1/messages")]
    [InlineData("GET", "/api/users/me")]
    public async Task Protected_endpoints_require_a_token(string method, string url)
    {
        var response = await factory.CreateBrowserClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), url)
        {
            Content = JsonContent.Create(new { content = "x" }),
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_forged_token_is_rejected()
    {
        var client = factory.CreateBrowserClient();
        var response = await client.SendAsync(Authorized(HttpMethod.Get, "/api/users/me",
            "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.Zm9yZ2Vk"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Users_cannot_read_change_or_delete_another_users_conversation()
    {
        var owner = factory.CreateBrowserClient();
        var ownerToken = await SignInAsync(factory, owner);
        var id = (await (await owner.SendAsync(Authorized(HttpMethod.Post, "/api/conversations", ownerToken, new { title = "Private" })))
            .Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("conversationId").GetInt32();
        await owner.SendAsync(Authorized(HttpMethod.Post, $"/api/chat/{id}/messages", ownerToken, new { content = "secret" }));

        var attacker = factory.CreateBrowserClient();
        var attackerToken = await SignInAsync(factory, attacker);

        var attempts = new[]
        {
            Authorized(HttpMethod.Get, $"/api/conversations/{id}", attackerToken),
            Authorized(HttpMethod.Put, $"/api/conversations/{id}", attackerToken, new { title = "hacked" }),
            Authorized(HttpMethod.Delete, $"/api/conversations/{id}", attackerToken),
            Authorized(HttpMethod.Get, $"/api/chat/{id}/messages", attackerToken),
            Authorized(HttpMethod.Post, $"/api/chat/{id}/messages", attackerToken, new { content = "hi" }),
        };
        foreach (var request in attempts)
        {
            var response = await attacker.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // 404, not 403: existence isn't revealed
        }

        var attackerList = await (await attacker.SendAsync(Authorized(HttpMethod.Get, "/api/conversations", attackerToken)))
            .Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(0, attackerList.GetProperty("totalCount").GetInt32());

        var stillThere = await factory.WithDbAsync(db => db.Conversations.SingleAsync(c => c.ConversationId == id));
        Assert.Equal("Private", stillThere.Title);
        Assert.Equal(2, await factory.WithDbAsync(db => db.Messages.CountAsync(m => m.ConversationId == id)));
    }

    [Fact]
    public async Task No_session_is_issued_before_the_otp_is_verified()
    {
        var client = factory.CreateBrowserClient();
        var email = NewEmail("pending");
        var sent = await client.PostAsJsonAsync("/api/auth/send-otp", new { email });
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var body = await sent.Content.ReadAsStringAsync();
        Assert.DoesNotContain("accessToken", body);
        Assert.DoesNotContain(factory.Email.LastCodeFor(email), body); // the code is only in the email

        // No account exists until the code is verified.
        Assert.False(await factory.WithDbAsync(db => db.Users.AnyAsync(u => u.Email == email)));

        // No refresh cookie was set, so there is no way to get a session yet.
        var refresh = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Invalid_google_token_and_guessed_codes_are_rejected()
    {
        var client = factory.CreateBrowserClient();
        var google = await client.PostAsJsonAsync("/api/auth/google", new { idToken = "not-a-google-token" });
        Assert.Equal(HttpStatusCode.Unauthorized, google.StatusCode);
        Assert.Empty(google.Headers.TryGetValues("Set-Cookie", out var c1) ? c1 : []);

        // A code for an address that never requested one.
        var verify = await client.PostAsJsonAsync("/api/auth/verify-otp", new { email = NewEmail("never"), code = "123456" });
        Assert.Equal(HttpStatusCode.BadRequest, verify.StatusCode);
        Assert.Empty(verify.Headers.TryGetValues("Set-Cookie", out var c2) ? c2 : []);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("two@@example.com")]
    public async Task Invalid_email_addresses_are_rejected(string email)
    {
        var response = await factory.CreateBrowserClient().PostAsJsonAsync("/api/auth/send-otp", new { email });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_otp_attempts_are_limited_and_then_even_the_right_code_fails()
    {
        var client = factory.CreateBrowserClient();
        var email = NewEmail("brute");
        (await client.PostAsJsonAsync("/api/auth/send-otp", new { email })).EnsureSuccessStatusCode();
        var right = factory.Email.LastCodeFor(email);
        var wrong = right == "000000" ? "111111" : "000000";

        for (var i = 0; i < 5; i++)
        {
            var attempt = await client.PostAsJsonAsync("/api/auth/verify-otp", new { email, code = wrong });
            Assert.Equal(HttpStatusCode.BadRequest, attempt.StatusCode);
        }

        var late = await client.PostAsJsonAsync("/api/auth/verify-otp", new { email, code = right });
        Assert.Equal(HttpStatusCode.BadRequest, late.StatusCode);
        Assert.False(await factory.WithDbAsync(db => db.Users.AnyAsync(u => u.Email == email)));
    }

    [Fact]
    public async Task Refresh_rotates_the_cookie_and_logout_ends_the_session()
    {
        var client = factory.CreateBrowserClient();
        await SignInAsync(factory, client);

        var refreshed = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var cookie = Assert.Single(refreshed.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);

        var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var afterLogout = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task Database_never_stores_plain_otps_or_refresh_tokens()
    {
        var client = factory.CreateBrowserClient();
        var email = $"storage-{Guid.NewGuid():N}@example.test";
        await SignInAsync(factory, client, email);
        var code = factory.Email.LastCodeFor(email);

        var hashes = await factory.WithDbAsync(db => db.OtpVerifications
            .Where(o => o.Email == email).Select(o => o.OtpHash).ToListAsync());
        Assert.NotEmpty(hashes);
        Assert.All(hashes, h => Assert.DoesNotContain(code, h));

        var tokenHashes = await factory.WithDbAsync(db => db.RefreshTokens
            .Where(r => r.User.Email == email).Select(r => r.TokenHash).ToListAsync());
        Assert.All(tokenHashes, h => Assert.Matches("^[0-9A-F]{64}$", h)); // SHA-256 hex, not the raw token
    }

    [Fact]
    public async Task Unknown_api_routes_return_json_404_and_security_headers_are_set()
    {
        var response = await factory.CreateBrowserClient().GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Profile_name_can_be_updated_but_not_blank()
    {
        var client = factory.CreateBrowserClient();
        var token = await SignInAsync(factory, client);

        var blank = await client.SendAsync(Authorized(HttpMethod.Put, "/api/users/me", token, new { fullName = "  " }));
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);

        var ok = await client.SendAsync(Authorized(HttpMethod.Put, "/api/users/me", token, new { fullName = " New Name " }));
        var me = await ok.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("New Name", me.GetProperty("fullName").GetString());
        Assert.True(me.GetProperty("isEmailVerified").GetBoolean());
    }
}
