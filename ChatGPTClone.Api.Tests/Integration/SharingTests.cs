using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ChatGPTClone.Api.Data;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ChatGPTClone.Api.Tests.Integration.TestAuth;

namespace ChatGPTClone.Api.Tests.Integration;

/// <summary>Roadmap feature 15: read-only share links (hashed tokens, snapshots, revocation, no file access).</summary>
[Collection(ApiCollection.Name)]
public sealed class SharingTests(ApiFactory factory)
{
    private sealed record User(HttpClient Client, string Token);

    private async Task<User> SignedInAsync()
    {
        var client = factory.CreateBrowserClient();
        return new User(client, await SignInAsync(factory, client));
    }

    private static Task<HttpResponseMessage> SendAsync(User user, HttpMethod method, string url, object? body = null) =>
        user.Client.SendAsync(Authorized(method, url, user.Token, body));

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Json);

    private static async Task<int> ChatWithAsync(User user, params string[] questions)
    {
        var id = (await JsonAsync(await SendAsync(user, HttpMethod.Post, "/api/conversations", new { }))).GetProperty("conversationId").GetInt32();
        foreach (var question in questions)
        {
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(user, HttpMethod.Post, $"/api/chat/{id}/messages", new { content = question })).StatusCode);
        }
        return id;
    }

    private static async Task<string> ShareAsync(User user, int conversationId)
    {
        var response = await SendAsync(user, HttpMethod.Post, $"/api/conversations/{conversationId}/share");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("token").GetString()!;
    }

    [Fact]
    public async Task Anyone_with_the_link_sees_a_snapshot_without_signing_in_and_later_messages_stay_private()
    {
        var owner = await SignedInAsync();
        var chat = await ChatWithAsync(owner, "What is a monad?");

        var token = await ShareAsync(owner, chat);
        Assert.Equal(43, token.Length); // 32 random bytes, base64url

        await SendAsync(owner, HttpMethod.Post, $"/api/chat/{chat}/messages", new { content = "Private follow-up" });

        var anonymous = factory.CreateBrowserClient();
        var response = await anonymous.GetAsync($"/api/share/{token}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("noindex, nofollow, noarchive", response.Headers.GetValues("X-Robots-Tag").Single());
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());

        var shared = await JsonAsync(response);
        var messages = shared.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(["user", "assistant"], messages.Select(m => m.GetProperty("role").GetString()));
        Assert.Equal("What is a monad?", messages[0].GetProperty("content").GetString());
        Assert.DoesNotContain("Private follow-up", await response.Content.ReadAsStringAsync());

        var status = await JsonAsync(await SendAsync(owner, HttpMethod.Get, $"/api/conversations/{chat}/share"));
        Assert.True(status.GetProperty("isShared").GetBoolean());
        Assert.Equal(2, status.GetProperty("messageCount").GetInt32());
    }

    [Fact]
    public async Task Only_a_hash_of_the_token_is_stored()
    {
        var owner = await SignedInAsync();
        var token = await ShareAsync(owner, await ChatWithAsync(owner, "Hi"));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expected = SHA256.HashData(WebEncoders.Base64UrlDecode(token));
        var link = await db.SharedLinks.SingleAsync(s => s.TokenHash == expected);
        Assert.DoesNotContain(token, link.SnapshotJson);
        Assert.Equal(32, link.TokenHash.Length);
    }

    [Fact]
    public async Task Revoked_replaced_wrong_and_deleted_links_all_return_404()
    {
        var owner = await SignedInAsync();
        var anonymous = factory.CreateBrowserClient();
        var chat = await ChatWithAsync(owner, "Hello");

        var first = await ShareAsync(owner, chat);
        var second = await ShareAsync(owner, chat); // sharing again replaces the link
        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/share/{first}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/share/{second}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(owner, HttpMethod.Delete, $"/api/conversations/{chat}/share")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(owner, HttpMethod.Delete, $"/api/conversations/{chat}/share")).StatusCode); // idempotent
        var revoked = await anonymous.GetAsync($"/api/share/{second}");
        Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        Assert.Equal("noindex, nofollow, noarchive", revoked.Headers.GetValues("X-Robots-Tag").Single());
        Assert.False((await JsonAsync(await SendAsync(owner, HttpMethod.Get, $"/api/conversations/{chat}/share"))).GetProperty("isShared").GetBoolean());

        foreach (var wrong in new[] { WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32)), "short", new string('!', 43), second[..42] + "A" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/share/{Uri.EscapeDataString(wrong)}")).StatusCode);
        }

        var deletedChat = await ChatWithAsync(owner, "Soon gone");
        var deletedToken = await ShareAsync(owner, deletedChat);
        await SendAsync(owner, HttpMethod.Delete, $"/api/conversations/{deletedChat}");
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/share/{deletedToken}")).StatusCode);
    }

    [Fact]
    public async Task Shared_pages_name_attachments_but_never_expose_file_ids_or_private_citation_markers()
    {
        var owner = await SignedInAsync();
        var upload = await UploadAsync(owner.Client, owner.Token, "memo.txt", "The deadline is 15 December."u8.ToArray());
        var file = await JsonAsync(upload);
        var fileId = file.GetProperty("fileId").GetGuid();
        var chat = (await JsonAsync(await SendAsync(owner, HttpMethod.Post, "/api/conversations", new { }))).GetProperty("conversationId").GetInt32();
        long chunkId;
        using (var scope = factory.Services.CreateScope())
        {
            chunkId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().DocumentChunks
                .Where(c => c.FileId == fileId).Select(c => c.ChunkId).FirstAsync();
        }
        try
        {
            factory.Ai.Reply = _ => $"It is 15 December [file:{chunkId}].";
            await SendAsync(owner, HttpMethod.Post, $"/api/chat/{chat}/messages", new { content = "When?", attachmentIds = new[] { fileId } });
        }
        finally
        {
            factory.Ai.Reply = null;
        }

        var token = await ShareAsync(owner, chat);
        var body = await (await factory.CreateBrowserClient().GetAsync($"/api/share/{token}")).Content.ReadAsStringAsync();

        Assert.Contains("memo.txt", body);                          // the attachment's name
        Assert.DoesNotContain(fileId.ToString(), body);             // but nothing that could fetch it
        Assert.DoesNotContain($"file:{chunkId}", body);             // nor the private passage id
        Assert.Contains("(memo.txt, ", body);                       // the citation as readable text instead
    }

    [Fact]
    public async Task Only_the_owner_can_share_check_or_stop_sharing_and_empty_chats_cannot_be_shared()
    {
        var owner = await SignedInAsync();
        var other = await SignedInAsync();
        var chat = await ChatWithAsync(owner, "Mine");

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(other, HttpMethod.Post, $"/api/conversations/{chat}/share")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(other, HttpMethod.Get, $"/api/conversations/{chat}/share")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(other, HttpMethod.Delete, $"/api/conversations/{chat}/share")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateBrowserClient().PostAsync($"/api/conversations/{chat}/share", null)).StatusCode);

        var empty = await ChatWithAsync(owner);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(owner, HttpMethod.Post, $"/api/conversations/{empty}/share")).StatusCode);
    }

    [Fact]
    public async Task The_public_endpoint_is_rate_limited_per_client()
    {
        var owner = await SignedInAsync();
        var token = await ShareAsync(owner, await ChatWithAsync(owner, "Hi"));
        // Same app and database, with a small limit (a fresh host has its own limiter state).
        using var limited = factory.WithWebHostBuilder(b => b.UseSetting("RateLimits:SharedLinksPerMinute", "3"));
        var anonymous = limited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            statuses.Add((await anonymous.GetAsync($"/api/share/{token}")).StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests], statuses);
    }
}
