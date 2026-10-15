using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChatGPTClone.Api.Interfaces;
using Microsoft.EntityFrameworkCore;
using static ChatGPTClone.Api.Tests.Integration.TestAuth;

namespace ChatGPTClone.Api.Tests.Integration;

[Collection(ApiCollection.Name)]
public class ChatFlowTests(ApiFactory factory)
{
    [Fact]
    public async Task Full_conversation_lifecycle_works_end_to_end()
    {
        var client = factory.CreateBrowserClient();
        var token = await SignInAsync(factory, client);

        // Create + first message: AI reply saved with token usage, title generated.
        var created = await client.SendAsync(Authorized(HttpMethod.Post, "/api/conversations", token, new { }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("conversationId").GetInt32();

        var sent = await client.SendAsync(Authorized(HttpMethod.Post, $"/api/chat/{id}/messages", token, new { content = "  Hello AI  " }));
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var reply = await sent.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("Hello AI", reply.GetProperty("userMessage").GetProperty("content").GetString());
        Assert.Equal("assistant", reply.GetProperty("assistantMessage").GetProperty("role").GetString());
        Assert.Equal("**Echo:** Hello AI", reply.GetProperty("assistantMessage").GetProperty("content").GetString());
        Assert.Equal(42, reply.GetProperty("assistantMessage").GetProperty("tokenUsage").GetInt32());
        Assert.Equal("Fake Generated Title", reply.GetProperty("conversation").GetProperty("title").GetString());

        // Second message: earlier history is sent to the AI as context.
        await client.SendAsync(Authorized(HttpMethod.Post, $"/api/chat/{id}/messages", token, new { content = "Second question" }));
        var lastPrompt = factory.Ai.Prompts.Last(p => p[^1].Content == "Second question");
        Assert.Contains(lastPrompt, m => m.Content == "Hello AI");

        // Messages come back oldest-first.
        var messages = await client.SendAsync(Authorized(HttpMethod.Get, $"/api/chat/{id}/messages?limit=50", token));
        var items = (await messages.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("items");
        Assert.Equal(["user", "assistant", "user", "assistant"],
            items.EnumerateArray().Select(m => m.GetProperty("role").GetString()).ToArray());

        // Rename shows up in the list.
        var renamed = await client.SendAsync(Authorized(HttpMethod.Put, $"/api/conversations/{id}", token, new { title = "  My chat  " }));
        Assert.Equal("My chat", (await renamed.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("title").GetString());
        var list = await client.SendAsync(Authorized(HttpMethod.Get, "/api/conversations?page=1&pageSize=20", token));
        var listJson = await list.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(1, listJson.GetProperty("totalCount").GetInt32());

        // Delete removes the conversation and (cascade) its messages.
        var deleted = await client.SendAsync(Authorized(HttpMethod.Delete, $"/api/conversations/{id}", token));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var after = await client.SendAsync(Authorized(HttpMethod.Get, $"/api/conversations/{id}", token));
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
        Assert.Equal(0, await factory.WithDbAsync(db => db.Messages.CountAsync(m => m.ConversationId == id)));
    }

    [Fact]
    public async Task Messages_page_backwards_with_before()
    {
        var client = factory.CreateBrowserClient();
        var token = await SignInAsync(factory, client);
        var id = (await (await client.SendAsync(Authorized(HttpMethod.Post, "/api/conversations", token, new { })))
            .Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("conversationId").GetInt32();
        for (var i = 1; i <= 3; i++)
        {
            await client.SendAsync(Authorized(HttpMethod.Post, $"/api/chat/{id}/messages", token, new { content = $"m{i}" }));
        }

        var newest = await (await client.SendAsync(Authorized(HttpMethod.Get, $"/api/chat/{id}/messages?limit=2", token)))
            .Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.True(newest.GetProperty("hasMore").GetBoolean());
        var oldestShown = newest.GetProperty("items")[0].GetProperty("messageId").GetInt64();

        var older = await (await client.SendAsync(Authorized(HttpMethod.Get, $"/api/chat/{id}/messages?limit=10&before={oldestShown}", token)))
            .Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(4, older.GetProperty("items").GetArrayLength());
        Assert.False(older.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task AI_failure_returns_a_clear_error_and_saves_nothing()
    {
        var client = factory.CreateBrowserClient();
        var token = await SignInAsync(factory, client);
        var id = (await (await client.SendAsync(Authorized(HttpMethod.Post, "/api/conversations", token, new { })))
            .Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("conversationId").GetInt32();

        factory.Ai.FailWith = AiFailureKind.QuotaExceeded;
        try
        {
            var response = await client.SendAsync(Authorized(HttpMethod.Post, $"/api/chat/{id}/messages", token, new { content = "hi" }));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
            Assert.Equal("ai_quota", problem.GetProperty("reason").GetString());
        }
        finally
        {
            factory.Ai.FailWith = null;
        }

        Assert.Equal(0, await factory.WithDbAsync(db => db.Messages.CountAsync(m => m.ConversationId == id)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_messages_are_rejected(string content)
    {
        var client = factory.CreateBrowserClient();
        var token = await SignInAsync(factory, client);
        var id = (await (await client.SendAsync(Authorized(HttpMethod.Post, "/api/conversations", token, new { })))
            .Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("conversationId").GetInt32();

        var response = await client.SendAsync(Authorized(HttpMethod.Post, $"/api/chat/{id}/messages", token, new { content }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Messages_longer_than_8000_characters_are_rejected()
    {
        var client = factory.CreateBrowserClient();
        var token = await SignInAsync(factory, client);
        var id = (await (await client.SendAsync(Authorized(HttpMethod.Post, "/api/conversations", token, new { })))
            .Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("conversationId").GetInt32();

        var response = await client.SendAsync(Authorized(HttpMethod.Post, $"/api/chat/{id}/messages", token, new { content = new string('x', 8001) }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
