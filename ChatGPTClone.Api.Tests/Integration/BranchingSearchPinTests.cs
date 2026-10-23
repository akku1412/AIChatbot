using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Models;
using Microsoft.EntityFrameworkCore;
using static ChatGPTClone.Api.Tests.Integration.TestAuth;

namespace ChatGPTClone.Api.Tests.Integration;

/// <summary>Roadmap Phase 1: reply versions, edit-and-resend branches, conversation search, pin and archive.</summary>
[Collection(ApiCollection.Name)]
public sealed class BranchingSearchPinTests(ApiFactory factory) : IDisposable
{
    public void Dispose() => factory.Ai.ResetStreaming();

    private sealed record User(HttpClient Client, string Token);

    private async Task<User> SignedInAsync()
    {
        var client = factory.CreateBrowserClient();
        return new User(client, await SignInAsync(factory, client));
    }

    private static async Task<int> NewConversationAsync(User user, string? title = null) =>
        (await (await user.Client.SendAsync(Authorized(HttpMethod.Post, "/api/conversations", user.Token, new { title })))
            .Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("conversationId").GetInt32();

    /// <summary>Sends a message through the streaming endpoint; returns the "done" event data.</summary>
    private static Task<JsonElement> SendAsync(User user, int conversationId, string content) =>
        StreamAsync(user, Authorized(HttpMethod.Post, $"/api/chat/{conversationId}/messages/stream", user.Token, new { content }));

    private static async Task<JsonElement> StreamAsync(User user, HttpRequestMessage request)
    {
        var response = await user.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        var done = text.Split("\n\n").Single(e => e.StartsWith("event: done", StringComparison.Ordinal));
        return JsonDocument.Parse(done[(done.IndexOf("data: ", StringComparison.Ordinal) + 6)..]).RootElement;
    }

    private static async Task<List<JsonElement>> ThreadAsync(User user, int conversationId) =>
        (await (await user.Client.SendAsync(Authorized(HttpMethod.Get, $"/api/chat/{conversationId}/messages", user.Token)))
            .Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("items").EnumerateArray().ToList();

    private static IEnumerable<string> Contents(IEnumerable<JsonElement> messages) =>
        messages.Select(m => m.GetProperty("content").GetString()!);

    private static long Id(JsonElement message) => message.GetProperty("messageId").GetInt64();

    private static IEnumerable<string> Turns(IReadOnlyList<AiChatMessage> prompt) =>
        prompt.Where(m => m.Role != MessageRole.System).Select(m => m.Content);

    // ---------- Regenerate (versions) ----------

    [Fact]
    public async Task Regenerate_by_message_id_keeps_versions_and_the_arrows_switch_between_them()
    {
        var user = await SignedInAsync();
        var conversationId = await NewConversationAsync(user);
        var first = await SendAsync(user, conversationId, "explain caching");
        var firstReplyId = Id(first.GetProperty("assistantMessage"));

        var second = await StreamAsync(user, Authorized(HttpMethod.Post, $"/api/messages/{firstReplyId}/regenerate", user.Token));
        var secondReply = second.GetProperty("assistantMessage");
        Assert.Equal(firstReplyId, second.GetProperty("replacedMessageId").GetInt64());
        Assert.Equal((2, 2), (secondReply.GetProperty("versionNumber").GetInt32(), secondReply.GetProperty("versionCount").GetInt32()));

        var thread = await ThreadAsync(user, conversationId);
        Assert.Equal([Id(first.GetProperty("userMessage")), Id(secondReply)], thread.Select(Id));
        Assert.Equal(2, thread[1].GetProperty("versionCount").GetInt32());
        Assert.Equal(1, thread[0].GetProperty("versionCount").GetInt32());

        // "‹" back to version 1.
        var switched = await user.Client.SendAsync(Authorized(HttpMethod.Post, $"/api/messages/{Id(secondReply)}/switch-version",
            user.Token, new { versionNumber = 1 }));
        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        var page = (await switched.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(firstReplyId, Id(page[1]));
        Assert.Equal(1, page[1].GetProperty("versionNumber").GetInt32());
        Assert.Equal(firstReplyId, Id((await ThreadAsync(user, conversationId))[1]));

        // A third version is numbered 3/3 even when generated while version 1 is shown.
        var third = await StreamAsync(user, Authorized(HttpMethod.Post, $"/api/messages/{firstReplyId}/regenerate", user.Token));
        Assert.Equal((3, 3), (third.GetProperty("assistantMessage").GetProperty("versionNumber").GetInt32(),
            third.GetProperty("assistantMessage").GetProperty("versionCount").GetInt32()));
    }

    [Fact]
    public async Task Regenerate_and_version_switching_validate_input_and_ownership()
    {
        var owner = await SignedInAsync();
        var other = await SignedInAsync();
        var conversationId = await NewConversationAsync(owner);
        var done = await SendAsync(owner, conversationId, "hello");
        var replyId = Id(done.GetProperty("assistantMessage"));

        Assert.Equal(HttpStatusCode.NotFound,
            (await other.Client.SendAsync(Authorized(HttpMethod.Post, $"/api/messages/{replyId}/regenerate", other.Token))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.Client.SendAsync(Authorized(HttpMethod.Post, $"/api/messages/{replyId}/switch-version", other.Token, new { versionNumber = 1 }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.Client.SendAsync(Authorized(HttpMethod.Post, "/api/messages/999999999/regenerate", owner.Token))).StatusCode);

        var outOfRange = await owner.Client.SendAsync(Authorized(HttpMethod.Post, $"/api/messages/{replyId}/switch-version",
            owner.Token, new { versionNumber = 5 }));
        Assert.Equal(HttpStatusCode.BadRequest, outOfRange.StatusCode);
        Assert.Equal("version_out_of_range", (await outOfRange.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("reason").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.SendAsync(Authorized(HttpMethod.Post,
            $"/api/messages/{replyId}/switch-version", owner.Token, new { versionNumber = 0 }))).StatusCode);
    }

    // ---------- Edit and resend (branches) ----------

    [Fact]
    public async Task Edit_creates_a_branch_the_ai_only_sees_the_active_branch_and_the_old_branch_is_kept()
    {
        var user = await SignedInAsync();
        var conversationId = await NewConversationAsync(user);
        var q1 = await SendAsync(user, conversationId, "my city is Pune");
        await SendAsync(user, conversationId, "what is my city?");
        var q1Id = Id(q1.GetProperty("userMessage"));

        // Edit the first question: a new branch starting at that point.
        var edited = await StreamAsync(user, Authorized(HttpMethod.Put, $"/api/messages/{q1Id}", user.Token, new { content = "my city is Jaipur" }));
        var newQuestion = edited.GetProperty("userMessage");
        Assert.Equal(q1Id, edited.GetProperty("replacedMessageId").GetInt64());
        Assert.Equal("my city is Jaipur", newQuestion.GetProperty("content").GetString());
        Assert.Equal((2, 2), (newQuestion.GetProperty("versionNumber").GetInt32(), newQuestion.GetProperty("versionCount").GetInt32()));
        Assert.Equal(["my city is Jaipur"], Turns(factory.Ai.LastPrompt!)); // nothing from the old branch

        Assert.Equal(["my city is Jaipur", "**Echo:** my city is Jaipur"], Contents(await ThreadAsync(user, conversationId)));

        // A follow-up continues the new branch only.
        await SendAsync(user, conversationId, "what is my city?");
        Assert.Equal(["my city is Jaipur", "**Echo:** my city is Jaipur", "what is my city?"], Turns(factory.Ai.LastPrompt!));

        // Switching the question back to version 1 restores the whole original branch.
        var page = await (await user.Client.SendAsync(Authorized(HttpMethod.Post, $"/api/messages/{Id(newQuestion)}/switch-version",
            user.Token, new { versionNumber = 1 }))).Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(["my city is Pune", "**Echo:** my city is Pune", "what is my city?", "**Echo:** what is my city?"],
            Contents(page.GetProperty("items").EnumerateArray()));

        // And the export follows the visible branch.
        var export = await user.Client.SendAsync(Authorized(HttpMethod.Get, $"/api/conversations/{conversationId}/export", user.Token));
        var text = Encoding.UTF8.GetString((await export.Content.ReadAsByteArrayAsync())[3..]);
        Assert.Contains("my city is Pune", text);
        Assert.DoesNotContain("Jaipur", text);
    }

    [Fact]
    public async Task Edit_keeps_attachments_and_validates_input_and_ownership()
    {
        var owner = await SignedInAsync();
        var other = await SignedInAsync();
        var fileId = (await (await UploadAsync(owner.Client, owner.Token, "notes.txt", "Budget is 500 rupees"u8.ToArray()))
            .Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("fileId").GetGuid();
        var conversationId = await NewConversationAsync(owner);
        var first = await StreamAsync(owner, Authorized(HttpMethod.Post, $"/api/chat/{conversationId}/messages/stream", owner.Token,
            new { content = "what is the budget?", attachmentIds = new[] { fileId } }));
        var questionId = Id(first.GetProperty("userMessage"));
        var replyId = Id(first.GetProperty("assistantMessage"));

        var edited = await StreamAsync(owner, Authorized(HttpMethod.Put, $"/api/messages/{questionId}", owner.Token,
            new { content = "how much is the budget?" }));
        Assert.Equal(fileId, edited.GetProperty("userMessage").GetProperty("attachments")[0].GetProperty("fileId").GetGuid());
        Assert.Contains("Budget is 500 rupees", factory.Ai.LastPrompt![0].Content); // document still in context

        Assert.Equal(HttpStatusCode.Conflict,
            (await owner.Client.SendAsync(Authorized(HttpMethod.Put, $"/api/messages/{replyId}", owner.Token, new { content = "x" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await owner.Client.SendAsync(Authorized(HttpMethod.Put, $"/api/messages/{questionId}", owner.Token, new { content = "  " }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.Client.SendAsync(Authorized(HttpMethod.Put, $"/api/messages/{questionId}", other.Token, new { content = "x" }))).StatusCode);
    }

    [Fact]
    public async Task A_failed_edit_saves_nothing_and_keeps_the_original_branch()
    {
        var user = await SignedInAsync();
        var conversationId = await NewConversationAsync(user);
        var first = await SendAsync(user, conversationId, "original question");
        factory.Ai.FailWith = AiFailureKind.Timeout;

        var response = await user.Client.SendAsync(Authorized(HttpMethod.Put, $"/api/messages/{Id(first.GetProperty("userMessage"))}",
            user.Token, new { content = "edited question" }));

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Equal(["original question", "**Echo:** original question"], Contents(await ThreadAsync(user, conversationId)));
        Assert.Equal(2, await factory.WithDbAsync(db => db.Messages.CountAsync(m => m.ConversationId == conversationId)));
    }

    // ---------- Search ----------

    [Fact]
    public async Task Search_finds_titles_and_message_text_with_a_snippet_for_the_owner_only()
    {
        var owner = await SignedInAsync();
        var other = await SignedInAsync();
        var trip = await NewConversationAsync(owner, "Goa trip plan");
        var budget = await NewConversationAsync(owner);
        await SendAsync(owner, budget, "The total marketing budget for Q3 is 4.5 lakh rupees and it covers ads, events and travel.");
        var otherChat = await NewConversationAsync(other, "Goa secrets");

        async Task<List<JsonElement>> Search(User user, string q) =>
            (await (await user.Client.SendAsync(Authorized(HttpMethod.Get, $"/api/conversations/search?q={Uri.EscapeDataString(q)}", user.Token)))
                .Content.ReadFromJsonAsync<JsonElement>(Json)).EnumerateArray().ToList();

        var goa = await Search(owner, "goa");
        var titleHit = Assert.Single(goa);
        Assert.Equal(trip, titleHit.GetProperty("conversationId").GetInt32());
        Assert.True(titleHit.GetProperty("titleMatches").GetBoolean());

        var marketing = Assert.Single(await Search(owner, "MARKETING budget"));
        Assert.Equal(budget, marketing.GetProperty("conversationId").GetInt32());
        Assert.False(marketing.GetProperty("titleMatches").GetBoolean());
        Assert.Contains("marketing budget for Q3", marketing.GetProperty("snippet").GetString());

        Assert.Empty(await Search(owner, "secrets"));          // other user's chat never appears
        Assert.Empty(await Search(owner, "4_5"));              // "_" is literal, not a wildcard
        Assert.NotEmpty(await Search(owner, "4.5 lakh"));
        Assert.DoesNotContain(await Search(other, "goa"), h => h.GetProperty("conversationId").GetInt32() == trip);
        Assert.Contains(await Search(other, "goa"), h => h.GetProperty("conversationId").GetInt32() == otherChat);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await owner.Client.SendAsync(Authorized(HttpMethod.Get, "/api/conversations/search?q=a", owner.Token))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.CreateBrowserClient().GetAsync("/api/conversations/search?q=goa")).StatusCode);
    }

    // ---------- Pin and archive ----------

    [Fact]
    public async Task Pinned_chats_come_first_and_archived_chats_move_to_their_own_list()
    {
        var user = await SignedInAsync();
        var older = await NewConversationAsync(user, "Older chat");
        var newer = await NewConversationAsync(user, "Newer chat");

        async Task<List<int>> ListAsync(bool archived = false) =>
            (await (await user.Client.SendAsync(Authorized(HttpMethod.Get, $"/api/conversations?archived={archived}", user.Token)))
                .Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("items").EnumerateArray()
                .Select(c => c.GetProperty("conversationId").GetInt32()).ToList();
        Task<HttpResponseMessage> Patch(int id, object body) =>
            user.Client.SendAsync(Authorized(HttpMethod.Patch, $"/api/conversations/{id}", user.Token, body));

        Assert.Equal([newer, older], await ListAsync());

        var pinned = await (await Patch(older, new { isPinned = true })).Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.True(pinned.GetProperty("isPinned").GetBoolean());
        Assert.Equal([older, newer], await ListAsync());

        var archived = await (await Patch(newer, new { archived = true })).Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.NotEqual(JsonValueKind.Null, archived.GetProperty("archivedAt").ValueKind);
        Assert.Equal([older], await ListAsync());
        Assert.Equal([newer], await ListAsync(archived: true));

        // Archived chats can still be opened and searched.
        Assert.Equal(HttpStatusCode.OK,
            (await user.Client.SendAsync(Authorized(HttpMethod.Get, $"/api/chat/{newer}/messages", user.Token))).StatusCode);

        await Patch(newer, new { archived = false });
        Assert.Equal([older, newer], await ListAsync());
        Assert.Empty(await ListAsync(archived: true));

        // Pin/archive don't count as activity (they don't reorder by recency); a rename through PATCH does.
        var before = await factory.WithDbAsync(db => db.Conversations.Where(c => c.ConversationId == older).Select(c => c.UpdatedAt).SingleAsync());
        await Patch(older, new { isPinned = false });
        await Patch(older, new { archived = true });
        await Patch(older, new { archived = false });
        var after = await factory.WithDbAsync(db => db.Conversations.SingleAsync(c => c.ConversationId == older));
        Assert.Equal(before, after.UpdatedAt);
        Assert.False(after.IsPinned);

        await Patch(older, new { title = "Renamed" });
        var renamed = await factory.WithDbAsync(db => db.Conversations.SingleAsync(c => c.ConversationId == older));
        Assert.Equal("Renamed", renamed.Title);
        Assert.True(renamed.UpdatedAt > before);
    }

    [Fact]
    public async Task Patch_validates_and_is_owner_only()
    {
        var owner = await SignedInAsync();
        var other = await SignedInAsync();
        var conversationId = await NewConversationAsync(owner);

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.SendAsync(Authorized(HttpMethod.Patch,
            $"/api/conversations/{conversationId}", other.Token, new { isPinned = true }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.SendAsync(Authorized(HttpMethod.Patch,
            $"/api/conversations/{conversationId}", owner.Token, new { }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.SendAsync(Authorized(HttpMethod.Patch,
            $"/api/conversations/{conversationId}", owner.Token, new { title = new string('x', 201) }))).StatusCode);
        Assert.False(await factory.WithDbAsync(db => db.Conversations.Where(c => c.ConversationId == conversationId).Select(c => c.IsPinned).SingleAsync()));
    }
}
