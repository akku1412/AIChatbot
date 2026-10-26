using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChatGPTClone.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ChatGPTClone.Api.Tests.Integration.TestAuth;

namespace ChatGPTClone.Api.Tests.Integration;

/// <summary>Roadmap feature 18: thumbs up/down (and optional comment) on AI replies.</summary>
[Collection(ApiCollection.Name)]
public sealed class FeedbackTests(ApiFactory factory)
{
    private sealed record User(HttpClient Client, string Token);

    private async Task<User> SignedInAsync()
    {
        var client = factory.CreateBrowserClient();
        return new User(client, await SignInAsync(factory, client));
    }

    private static Task<HttpResponseMessage> SendAsync(User user, HttpMethod method, string url, object? body = null) =>
        user.Client.SendAsync(Authorized(method, url, user.Token, body));

    /// <summary>One exchange; returns (conversation, question id, reply id).</summary>
    private static async Task<(int Chat, long Question, long Reply)> ExchangeAsync(User user)
    {
        var chat = (await (await SendAsync(user, HttpMethod.Post, "/api/conversations", new { }))
            .Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("conversationId").GetInt32();
        var sent = await (await SendAsync(user, HttpMethod.Post, $"/api/chat/{chat}/messages", new { content = "Explain DNS" }))
            .Content.ReadFromJsonAsync<JsonElement>(Json);
        return (chat, sent.GetProperty("userMessage").GetProperty("messageId").GetInt64(),
            sent.GetProperty("assistantMessage").GetProperty("messageId").GetInt64());
    }

    private static async Task<string?> FeedbackShownAsync(User user, int chat, long reply)
    {
        var page = await (await SendAsync(user, HttpMethod.Get, $"/api/chat/{chat}/messages")).Content.ReadFromJsonAsync<JsonElement>(Json);
        var message = page.GetProperty("items").EnumerateArray().Single(m => m.GetProperty("messageId").GetInt64() == reply);
        return message.GetProperty("feedback").GetString();
    }

    [Fact]
    public async Task A_reply_can_be_rated_changed_and_unrated_and_the_rating_comes_back_with_the_messages()
    {
        var user = await SignedInAsync();
        var (chat, _, reply) = await ExchangeAsync(user);
        Assert.Null(await FeedbackShownAsync(user, chat, reply));

        var up = await SendAsync(user, HttpMethod.Post, $"/api/messages/{reply}/feedback", new { rating = "up" });
        Assert.Equal(HttpStatusCode.OK, up.StatusCode);
        Assert.Equal("up", await FeedbackShownAsync(user, chat, reply));

        var down = await SendAsync(user, HttpMethod.Post, $"/api/messages/{reply}/feedback", new { rating = "down", comment = "  Too vague.  " });
        var saved = await down.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("down", saved.GetProperty("rating").GetString());
        Assert.Equal("Too vague.", saved.GetProperty("comment").GetString());
        Assert.Equal("down", await FeedbackShownAsync(user, chat, reply));

        using (var scope = factory.Services.CreateScope())
        {
            var rows = await scope.ServiceProvider.GetRequiredService<AppDbContext>().MessageFeedback.Where(f => f.MessageId == reply).ToListAsync();
            var row = Assert.Single(rows); // one rating per reply, updated in place
            Assert.Equal(-1, row.Rating);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(user, HttpMethod.Delete, $"/api/messages/{reply}/feedback")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(user, HttpMethod.Delete, $"/api/messages/{reply}/feedback")).StatusCode);
        Assert.Null(await FeedbackShownAsync(user, chat, reply));
    }

    [Fact]
    public async Task Only_ai_replies_can_be_rated_and_bad_input_is_refused()
    {
        var user = await SignedInAsync();
        var (_, question, reply) = await ExchangeAsync(user);

        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(user, HttpMethod.Post, $"/api/messages/{question}/feedback", new { rating = "up" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(user, HttpMethod.Post, $"/api/messages/{reply}/feedback", new { rating = "meh" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(user, HttpMethod.Post, $"/api/messages/{reply}/feedback",
            new { rating = "down", comment = new string('x', 1001) })).StatusCode);
    }

    [Fact]
    public async Task Other_users_cannot_rate_or_unrate_your_replies()
    {
        var owner = await SignedInAsync();
        var other = await SignedInAsync();
        var (chat, _, reply) = await ExchangeAsync(owner);
        await SendAsync(owner, HttpMethod.Post, $"/api/messages/{reply}/feedback", new { rating = "up" });

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(other, HttpMethod.Post, $"/api/messages/{reply}/feedback", new { rating = "down" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(other, HttpMethod.Delete, $"/api/messages/{reply}/feedback")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(owner, HttpMethod.Post, "/api/messages/999999999/feedback", new { rating = "up" })).StatusCode);
        Assert.Equal("up", await FeedbackShownAsync(owner, chat, reply));
    }

    [Fact]
    public async Task Deleting_the_conversation_deletes_its_feedback()
    {
        var user = await SignedInAsync();
        var (chat, _, reply) = await ExchangeAsync(user);
        await SendAsync(user, HttpMethod.Post, $"/api/messages/{reply}/feedback", new { rating = "down", comment = "wrong" });

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(user, HttpMethod.Delete, $"/api/conversations/{chat}")).StatusCode);

        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().MessageFeedback.AnyAsync(f => f.MessageId == reply));
    }
}
