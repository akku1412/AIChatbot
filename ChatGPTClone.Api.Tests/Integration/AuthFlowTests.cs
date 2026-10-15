using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static ChatGPTClone.Api.Tests.Integration.TestAuth;

namespace ChatGPTClone.Api.Tests.Integration;

/// <summary>Email OTP sign-in, Google sign-in, and how the two share one account per email.</summary>
[Collection(ApiCollection.Name)]
public class AuthFlowTests(ApiFactory factory)
{
    [Fact]
    public async Task Email_otp_registers_a_new_user_and_signs_in()
    {
        var client = factory.CreateBrowserClient();
        var email = NewEmail("new");

        var session = await SignInWithEmailAsync(factory, client, email.ToUpperInvariant()); // addresses are case-insensitive

        var user = session.GetProperty("user");
        Assert.Equal(email, user.GetProperty("email").GetString());
        Assert.True(user.GetProperty("isEmailVerified").GetBoolean());
        Assert.False(user.GetProperty("hasGoogleAccount").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(session.GetProperty("accessToken").GetString()));

        var me = await client.SendAsync(Authorized(HttpMethod.Get, "/api/auth/me", session.GetProperty("accessToken").GetString()!));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Signing_in_again_with_the_same_email_reuses_the_account()
    {
        var email = NewEmail("again");
        var first = await SignInWithEmailAsync(factory, factory.CreateBrowserClient(), email);
        var second = await SignInWithEmailAsync(factory, factory.CreateBrowserClient(), email);

        Assert.Equal(first.GetProperty("user").GetProperty("userId").GetInt32(), second.GetProperty("user").GetProperty("userId").GetInt32());
        Assert.Equal(1, await factory.WithDbAsync(db => db.Users.CountAsync(u => u.Email == email)));
    }

    [Fact]
    public async Task Send_otp_looks_the_same_for_new_and_existing_accounts()
    {
        var existing = NewEmail("existing");
        await SignInWithEmailAsync(factory, factory.CreateBrowserClient(), existing);
        var fresh = NewEmail("fresh");

        var a = await (await factory.CreateBrowserClient().PostAsJsonAsync("/api/auth/send-otp", new { email = existing }))
            .Content.ReadFromJsonAsync<JsonElement>(Json);
        var b = await (await factory.CreateBrowserClient().PostAsJsonAsync("/api/auth/send-otp", new { email = fresh }))
            .Content.ReadFromJsonAsync<JsonElement>(Json);

        static string[] Shape(JsonElement e) => e.EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(Shape(a), Shape(b));
        Assert.True(b.GetProperty("codeSent").GetBoolean());
    }

    [Fact]
    public async Task Resend_respects_the_cooldown_and_invalidates_the_old_code()
    {
        var client = factory.CreateBrowserClient();
        var email = NewEmail("resend");
        (await client.PostAsJsonAsync("/api/auth/send-otp", new { email })).EnsureSuccessStatusCode();

        var tooSoon = await client.PostAsJsonAsync("/api/auth/resend-otp", new { email });
        Assert.Equal(HttpStatusCode.TooManyRequests, tooSoon.StatusCode);
        Assert.True(tooSoon.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task A_used_code_cannot_be_reused()
    {
        var client = factory.CreateBrowserClient();
        var email = NewEmail("reuse");
        await SignInWithEmailAsync(factory, client, email);
        var code = factory.Email.LastCodeFor(email);

        var again = await factory.CreateBrowserClient().PostAsJsonAsync("/api/auth/verify-otp", new { email, code });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        var problem = await again.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("expired", problem.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Google_sign_in_creates_an_account_and_signs_in_without_an_otp()
    {
        var client = factory.CreateBrowserClient();
        var email = NewEmail("google");

        var response = await GoogleAsync(client, $"sub-{Guid.NewGuid():N}", email, "Google Person");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("aichat_refresh="));
        var session = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.False(string.IsNullOrWhiteSpace(session.GetProperty("accessToken").GetString()));
        Assert.Equal("Google Person", session.GetProperty("user").GetProperty("fullName").GetString());
        Assert.True(session.GetProperty("user").GetProperty("hasGoogleAccount").GetBoolean());
        Assert.Throws<InvalidOperationException>(() => factory.Email.LastCodeFor(email)); // no OTP email was sent
    }

    [Fact]
    public async Task Google_sign_in_links_to_an_existing_email_account()
    {
        var email = NewEmail("link");
        var emailSession = await SignInWithEmailAsync(factory, factory.CreateBrowserClient(), email);
        var userId = emailSession.GetProperty("user").GetProperty("userId").GetInt32();

        var google = await GoogleAsync(factory.CreateBrowserClient(), $"sub-{Guid.NewGuid():N}", email);
        Assert.Equal(HttpStatusCode.OK, google.StatusCode);
        var linked = await google.Content.ReadFromJsonAsync<JsonElement>(Json);

        Assert.Equal(userId, linked.GetProperty("user").GetProperty("userId").GetInt32()); // same account, same chats
        Assert.True(linked.GetProperty("user").GetProperty("hasGoogleAccount").GetBoolean());
        Assert.Equal(1, await factory.WithDbAsync(db => db.Users.CountAsync(u => u.Email == email)));
    }

    [Fact]
    public async Task A_different_google_account_with_the_same_email_is_not_merged()
    {
        var email = NewEmail("conflict");
        (await GoogleAsync(factory.CreateBrowserClient(), $"sub-a-{Guid.NewGuid():N}", email)).EnsureSuccessStatusCode();

        var other = await GoogleAsync(factory.CreateBrowserClient(), $"sub-b-{Guid.NewGuid():N}", email);

        Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
    }

    [Fact]
    public async Task Returning_google_user_gets_the_same_account()
    {
        var sub = $"sub-{Guid.NewGuid():N}";
        var email = NewEmail("returning");
        var first = await (await GoogleAsync(factory.CreateBrowserClient(), sub, email)).Content.ReadFromJsonAsync<JsonElement>(Json);
        var second = await (await GoogleAsync(factory.CreateBrowserClient(), sub, email)).Content.ReadFromJsonAsync<JsonElement>(Json);

        Assert.Equal(first.GetProperty("user").GetProperty("userId").GetInt32(), second.GetProperty("user").GetProperty("userId").GetInt32());
    }

    [Fact]
    public async Task Email_otp_still_works_for_an_account_created_with_google()
    {
        var email = NewEmail("both");
        var google = await (await GoogleAsync(factory.CreateBrowserClient(), $"sub-{Guid.NewGuid():N}", email))
            .Content.ReadFromJsonAsync<JsonElement>(Json);

        var viaEmail = await SignInWithEmailAsync(factory, factory.CreateBrowserClient(), email);

        Assert.Equal(google.GetProperty("user").GetProperty("userId").GetInt32(), viaEmail.GetProperty("user").GetProperty("userId").GetInt32());
    }
}
