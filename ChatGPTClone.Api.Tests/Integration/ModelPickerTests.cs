using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChatGPTClone.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ChatGPTClone.Api.Tests.Integration.TestAuth;

namespace ChatGPTClone.Api.Tests.Integration;

/// <summary>Roadmap feature 12: per-chat model picker over the Ai:Models allow-list ("fast", "smart"; default "smart").</summary>
[Collection(ApiCollection.Name)]
public sealed class ModelPickerTests(ApiFactory factory) : IDisposable
{
    public void Dispose() => factory.Ai.ResetStreaming();

    private sealed record User(HttpClient Client, string Token);

    private async Task<User> SignedInAsync()
    {
        var client = factory.CreateBrowserClient();
        return new User(client, await SignInAsync(factory, client));
    }

    private static async Task<HttpResponseMessage> CreateAsync(User user, object body) =>
        await user.Client.SendAsync(Authorized(HttpMethod.Post, "/api/conversations", user.Token, body));

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Json);

    private static Task<HttpResponseMessage> PatchAsync(User user, int id, object body) =>
        user.Client.SendAsync(Authorized(HttpMethod.Patch, $"/api/conversations/{id}", user.Token, body));

    /// <summary>Streams one message and returns the saved reply.</summary>
    private static async Task<JsonElement> SendAsync(User user, int conversationId, string content)
    {
        var response = await user.Client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/chat/{conversationId}/messages/stream", user.Token, new { content }),
            HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        var done = text.Split("\n\n").Single(e => e.StartsWith("event: done", StringComparison.Ordinal));
        return JsonDocument.Parse(done[(done.IndexOf("data: ", StringComparison.Ordinal) + 6)..]).RootElement
            .GetProperty("assistantMessage");
    }

    private async Task<string?> SavedModelIdAsync(long messageId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Messages.Where(m => m.MessageId == messageId).Select(m => m.ModelId).SingleAsync();
    }

    [Fact]
    public async Task Lists_the_allow_list_by_id_and_label_without_provider_model_names()
    {
        var user = await SignedInAsync();

        var response = await user.Client.SendAsync(Authorized(HttpMethod.Get, "/api/models", user.Token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var models = JsonDocument.Parse(body).RootElement;
        Assert.Equal("smart", models.GetProperty("defaultModelId").GetString());
        Assert.Equal(["fast", "smart"], models.GetProperty("models").EnumerateArray().Select(m => m.GetProperty("id").GetString()));
        Assert.True(models.GetProperty("models")[1].GetProperty("supportsVision").GetBoolean());
        Assert.DoesNotContain("model-name", body); // the provider's model names stay on the server
    }

    [Fact]
    public async Task Models_need_sign_in()
    {
        var response = await factory.CreateBrowserClient().GetAsync("/api/models");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Each_chat_answers_with_its_own_model_and_records_it_on_the_reply()
    {
        var user = await SignedInAsync();

        // No pick: the default model.
        var plain = (await JsonAsync(await CreateAsync(user, new { }))).GetProperty("conversationId").GetInt32();
        var first = await SendAsync(user, plain, "Hello");
        Assert.Equal("smart", factory.Ai.LastModelId);
        Assert.Equal("smart", await SavedModelIdAsync(first.GetProperty("messageId").GetInt64()));

        // Picked at creation.
        var created = await JsonAsync(await CreateAsync(user, new { modelId = "fast" }));
        Assert.Equal("fast", created.GetProperty("modelId").GetString());
        var fastReply = await SendAsync(user, created.GetProperty("conversationId").GetInt32(), "Hi");
        Assert.Equal("fast", factory.Ai.LastModelId);
        Assert.Equal("fast", await SavedModelIdAsync(fastReply.GetProperty("messageId").GetInt64()));

        // Switched mid-chat: applies from the next reply, and the earlier reply keeps its model.
        var patched = await PatchAsync(user, plain, new { modelId = "fast" });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        Assert.Equal("fast", (await JsonAsync(patched)).GetProperty("modelId").GetString());
        var second = await SendAsync(user, plain, "And now?");
        Assert.Equal("fast", factory.Ai.LastModelId);
        Assert.Equal("fast", await SavedModelIdAsync(second.GetProperty("messageId").GetInt64()));
        Assert.Equal("smart", await SavedModelIdAsync(first.GetProperty("messageId").GetInt64()));

        // The choice is persisted on the conversation.
        var reloaded = await JsonAsync(await user.Client.SendAsync(Authorized(HttpMethod.Get, $"/api/conversations/{plain}", user.Token)));
        Assert.Equal("fast", reloaded.GetProperty("modelId").GetString());
    }

    [Theory]
    [InlineData("gpt-4o")]                 // a raw provider model name
    [InlineData("smart-model-name")]       // a configured model's name instead of its id
    [InlineData("FAST")]                   // ids are exact
    [InlineData("")]
    public async Task Models_off_the_allow_list_are_rejected(string modelId)
    {
        var user = await SignedInAsync();
        var id = (await JsonAsync(await CreateAsync(user, new { }))).GetProperty("conversationId").GetInt32();

        var create = await CreateAsync(user, new { modelId });
        var patch = await PatchAsync(user, id, new { modelId });

        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);
        Assert.Contains("isn't available", await patch.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_chat_whose_model_was_removed_from_the_config_falls_back_to_the_default()
    {
        var user = await SignedInAsync();
        var id = (await JsonAsync(await CreateAsync(user, new { }))).GetProperty("conversationId").GetInt32();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Conversations.Where(c => c.ConversationId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ModelId, "retired-model"));
        }

        var reply = await SendAsync(user, id, "Still there?");

        Assert.Equal("smart", factory.Ai.LastModelId);
        Assert.Equal("smart", await SavedModelIdAsync(reply.GetProperty("messageId").GetInt64()));
    }

    [Fact]
    public async Task Another_users_chat_cannot_be_switched()
    {
        var owner = await SignedInAsync();
        var other = await SignedInAsync();
        var id = (await JsonAsync(await CreateAsync(owner, new { }))).GetProperty("conversationId").GetInt32();

        var response = await PatchAsync(other, id, new { modelId = "fast" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
