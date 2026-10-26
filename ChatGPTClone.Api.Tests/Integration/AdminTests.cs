using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ChatGPTClone.Api.Authentication;
using ChatGPTClone.Api.Data;
using ChatGPTClone.Api.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ChatGPTClone.Api.Tests.Integration.TestAuth;

namespace ChatGPTClone.Api.Tests.Integration;

/// <summary>Roadmap feature 22: roles, blocking, daily usage, quotas and the admin endpoints.</summary>
[Collection(ApiCollection.Name)]
public sealed class AdminTests(ApiFactory factory)
{
    private sealed record User(HttpClient Client, string Token, string Email);

    private static async Task<User> SignedInAsync(WebApplicationFactory<Program> host, ApiFactory fakes, string? email = null)
    {
        email ??= NewEmail();
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        return new User(client, await SignInAsync(fakes, client, email), email);
    }

    private Task<User> SignedInAsync(string? email = null) => SignedInAsync(factory, factory, email);

    private Task<User> AdminAsync() => SignedInAsync(ApiFactory.AdminEmail);

    private static Task<HttpResponseMessage> SendAsync(User user, HttpMethod method, string url, object? body = null) =>
        user.Client.SendAsync(Authorized(method, url, user.Token, body));

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    private static async Task<int> NewChatAsync(User user) =>
        (await JsonAsync(await SendAsync(user, HttpMethod.Post, "/api/conversations", new { }))).GetProperty("conversationId").GetInt32();

    private static async Task<JsonElement> FindUserAsync(User admin, string email)
    {
        var page = await JsonAsync(await SendAsync(admin, HttpMethod.Get, $"/api/admin/users?search={Uri.EscapeDataString(email)}"));
        return Assert.Single(page.GetProperty("items").EnumerateArray());
    }

    private static async Task<string?> ReasonAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).TryGetProperty("reason", out var reason) ? reason.GetString() : null;

    // ---------- Roles ----------

    [Fact]
    public async Task Admin_endpoints_need_the_admin_role()
    {
        var user = await SignedInAsync();
        foreach (var url in new[] { "/api/admin/users", "/api/admin/usage", "/api/admin/feedback" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(user, HttpMethod.Get, url)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(url)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(user, HttpMethod.Post, "/api/admin/users/1/block")).StatusCode);

        var me = await JsonAsync(await SendAsync(user, HttpMethod.Get, "/api/users/me"));
        Assert.Equal("User", me.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Listed_admin_email_becomes_admin_and_a_demotion_applies_to_the_open_session()
    {
        var admin = await AdminAsync();
        var me = await JsonAsync(await SendAsync(admin, HttpMethod.Get, "/api/users/me"));
        Assert.Equal("Admin", me.GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(admin, HttpMethod.Get, "/api/admin/users")).StatusCode);

        // Demoted in the database: the same (still valid) token loses admin access, without signing in again.
        var adminId = me.GetProperty("userId").GetInt32();
        await SetRoleAsync(adminId, UserRoles.User);
        try
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(admin, HttpMethod.Get, "/api/admin/users")).StatusCode);
        }
        finally
        {
            await SetRoleAsync(adminId, UserRoles.Admin);
        }
    }

    private async Task SetRoleAsync(int userId, string role)
    {
        await factory.WithDbAsync(db => db.Users.Where(u => u.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, role)));
        factory.Services.GetRequiredService<IUserAccessCache>().Invalidate(userId);
    }

    // ---------- Users and blocking ----------

    [Fact]
    public async Task Admin_lists_and_searches_users()
    {
        var admin = await AdminAsync();
        var user = await SignedInAsync();

        var found = await FindUserAsync(admin, user.Email);
        Assert.Equal(user.Email, found.GetProperty("email").GetString());
        Assert.Equal("User", found.GetProperty("role").GetString());
        Assert.False(found.GetProperty("isBlocked").GetBoolean());

        var page = await JsonAsync(await SendAsync(admin, HttpMethod.Get, "/api/admin/users?page=1&pageSize=2"));
        Assert.True(page.GetProperty("total").GetInt32() >= 2);
        Assert.Equal(2, page.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(admin, HttpMethod.Get, "/api/admin/users?pageSize=1000")).StatusCode);
    }

    [Fact]
    public async Task A_blocked_user_is_refused_everywhere_until_unblocked()
    {
        var admin = await AdminAsync();
        var user = await SignedInAsync();
        var userId = (await FindUserAsync(admin, user.Email)).GetProperty("userId").GetInt32();

        var blocked = await JsonAsync(await SendAsync(admin, HttpMethod.Post, $"/api/admin/users/{userId}/block"));
        Assert.True(blocked.GetProperty("isBlocked").GetBoolean());

        // The open session: every protected endpoint says so at once.
        var me = await SendAsync(user, HttpMethod.Get, "/api/users/me");
        Assert.Equal(HttpStatusCode.Forbidden, me.StatusCode);
        Assert.Equal("account_blocked", await ReasonAsync(me));
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(user, HttpMethod.Get, "/api/conversations")).StatusCode);

        // The session can't be renewed (refresh tokens revoked) and signing in again is refused.
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.Client.PostAsync("/api/auth/refresh", null)).StatusCode);
        var client = factory.CreateBrowserClient();
        (await client.PostAsJsonAsync("/api/auth/send-otp", new { email = user.Email })).EnsureSuccessStatusCode();
        var signIn = await client.PostAsJsonAsync("/api/auth/verify-otp", new { email = user.Email, code = factory.Email.LastCodeFor(user.Email) });
        Assert.Equal(HttpStatusCode.Forbidden, signIn.StatusCode);
        Assert.Equal("account_blocked", await ReasonAsync(signIn));

        // Logout (anonymous) still works for a blocked user.
        Assert.True((await user.Client.PostAsync("/api/auth/logout", null)).IsSuccessStatusCode);

        var unblocked = await JsonAsync(await SendAsync(admin, HttpMethod.Post, $"/api/admin/users/{userId}/unblock"));
        Assert.False(unblocked.GetProperty("isBlocked").GetBoolean());
        var again = await SignedInAsync(user.Email);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(again, HttpMethod.Get, "/api/users/me")).StatusCode);
    }

    [Fact]
    public async Task Admins_cannot_block_themselves_and_unknown_users_are_404()
    {
        var admin = await AdminAsync();
        var adminId = (await JsonAsync(await SendAsync(admin, HttpMethod.Get, "/api/users/me"))).GetProperty("userId").GetInt32();

        var self = await SendAsync(admin, HttpMethod.Post, $"/api/admin/users/{adminId}/block");
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        Assert.Equal("cannot_block_self", await ReasonAsync(self));
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(admin, HttpMethod.Post, "/api/admin/users/999999999/block")).StatusCode);
    }

    // ---------- Usage ----------

    [Fact]
    public async Task Messages_tokens_and_uploads_are_counted_per_user_per_day()
    {
        var admin = await AdminAsync();
        var user = await SignedInAsync();
        var chat = await NewChatAsync(user);
        await JsonAsync(await SendAsync(user, HttpMethod.Post, $"/api/chat/{chat}/messages", new { content = "Explain DNS briefly" }));
        Assert.Equal(HttpStatusCode.Created,
            (await UploadRawAsync(user.Client, user.Token, "notes.txt", Encoding.UTF8.GetBytes("some notes"))).StatusCode);

        var row = await FindUserAsync(admin, user.Email);
        Assert.Equal(1, row.GetProperty("messagesToday").GetInt32());
        Assert.True(row.GetProperty("tokensToday").GetInt64() > 0);
        Assert.Equal(1, row.GetProperty("uploadsToday").GetInt32());

        var usage = await JsonAsync(await SendAsync(admin, HttpMethod.Get, "/api/admin/usage?days=7"));
        var days = usage.GetProperty("days").EnumerateArray().ToList();
        Assert.Equal(7, days.Count);
        var today = days[^1];
        Assert.Equal(DateTime.UtcNow.Date, today.GetProperty("date").GetDateTime().Date);
        Assert.True(today.GetProperty("messages").GetInt32() >= 1);
        Assert.True(today.GetProperty("inputTokens").GetInt64() > 0);
        Assert.True(usage.GetProperty("totals").GetProperty("estimatedCost").GetDecimal() >= 0);
        var topUsers = usage.GetProperty("topUsers").EnumerateArray().ToList();
        Assert.InRange(topUsers.Count, 1, 10);
        Assert.True(topUsers.Zip(topUsers.Skip(1)).All(p => p.First.GetProperty("tokens").GetInt64() >= p.Second.GetProperty("tokens").GetInt64()));
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(admin, HttpMethod.Get, "/api/admin/usage?days=0")).StatusCode);
    }

    [Fact]
    public async Task Daily_token_quota_returns_429_with_a_clear_message()
    {
        await using var host = factory.WithWebHostBuilder(b => b.UseSetting("Usage:DailyTokenQuota", "1"));
        var user = await SignedInAsync(host, factory);
        var chat = await NewChatAsync(user);

        // Under the quota before the first message; that message uses it up.
        await JsonAsync(await SendAsync(user, HttpMethod.Post, $"/api/chat/{chat}/messages", new { content = "Hello" }));

        foreach (var url in new[] { $"/api/chat/{chat}/messages", $"/api/chat/{chat}/messages/stream" })
        {
            var refused = await SendAsync(user, HttpMethod.Post, url, new { content = "Again" });
            Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
            var problem = await refused.Content.ReadFromJsonAsync<JsonElement>(Json);
            Assert.Equal("quota_exceeded", problem.GetProperty("reason").GetString());
            Assert.Equal("tokens", problem.GetProperty("quota").GetString());
            Assert.Contains("today's limit", problem.GetProperty("detail").GetString());
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await SendAsync(user, HttpMethod.Post, $"/api/chat/{chat}/messages/regenerate")).StatusCode);
    }

    [Fact]
    public async Task Daily_upload_quota_returns_429()
    {
        await using var host = factory.WithWebHostBuilder(b => b.UseSetting("Usage:DailyUploadQuota", "1"));
        var user = await SignedInAsync(host, factory);

        Assert.Equal(HttpStatusCode.Created, (await UploadRawAsync(user.Client, user.Token, "a.txt", "first"u8.ToArray())).StatusCode);
        var refused = await UploadRawAsync(user.Client, user.Token, "b.txt", "second"u8.ToArray());

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("quota_exceeded", await ReasonAsync(refused));
    }

    // ---------- Feedback review ----------

    [Fact]
    public async Task Admin_sees_ratings_with_the_question_and_reply()
    {
        var admin = await AdminAsync();
        var user = await SignedInAsync();
        var chat = await NewChatAsync(user);
        var sent = await JsonAsync(await SendAsync(user, HttpMethod.Post, $"/api/chat/{chat}/messages", new { content = "What is a feedback test?" }));
        var reply = sent.GetProperty("assistantMessage").GetProperty("messageId").GetInt64();
        var comment = $"Too vague {Guid.NewGuid():N}";
        await JsonAsync(await SendAsync(user, HttpMethod.Post, $"/api/messages/{reply}/feedback", new { rating = "down", comment }));

        var page = await JsonAsync(await SendAsync(admin, HttpMethod.Get, "/api/admin/feedback?rating=-1&pageSize=100"));
        var item = Assert.Single(page.GetProperty("items").EnumerateArray(), f => f.GetProperty("comment").GetString() == comment);
        Assert.Equal(-1, item.GetProperty("rating").GetInt16());
        Assert.Equal(user.Email, item.GetProperty("userEmail").GetString());
        Assert.Equal("What is a feedback test?", item.GetProperty("question").GetString());
        Assert.False(string.IsNullOrEmpty(item.GetProperty("reply").GetString()));
        Assert.True(page.GetProperty("notHelpful").GetInt32() >= 1);

        Assert.DoesNotContain((await JsonAsync(await SendAsync(admin, HttpMethod.Get, "/api/admin/feedback?rating=1&pageSize=100")))
            .GetProperty("items").EnumerateArray(), f => f.GetProperty("messageId").GetInt64() == reply);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(admin, HttpMethod.Get, "/api/admin/feedback?rating=5")).StatusCode);
    }
}
