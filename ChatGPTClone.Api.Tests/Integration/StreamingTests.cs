using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Models;
using Microsoft.EntityFrameworkCore;
using static ChatGPTClone.Api.Tests.Integration.TestAuth;

namespace ChatGPTClone.Api.Tests.Integration;

/// <summary>Streaming replies (SSE), stop generation and regenerate, through the real API.</summary>
[Collection(ApiCollection.Name)]
public sealed class StreamingTests(ApiFactory factory) : IDisposable
{
    public void Dispose() => factory.Ai.ResetStreaming();

    private sealed record SseEvent(string Name, JsonElement Data);

    private async Task<(HttpClient Client, string Token, int ConversationId)> ConversationAsync()
    {
        var client = factory.CreateBrowserClient();
        var token = await SignInAsync(factory, client);
        var created = await (await client.SendAsync(Authorized(HttpMethod.Post, "/api/conversations", token, new { })))
            .Content.ReadFromJsonAsync<JsonElement>(Json);
        return (client, token, created.GetProperty("conversationId").GetInt32());
    }

    private static HttpRequestMessage StreamRequest(int conversationId, string token, string content) =>
        Authorized(HttpMethod.Post, $"/api/chat/{conversationId}/messages/stream", token, new { content });

    private static async Task<List<SseEvent>> ReadAllEventsAsync(HttpResponseMessage response)
    {
        var events = new List<SseEvent>();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        while (await ReadEventAsync(reader) is { } e)
        {
            events.Add(e);
        }
        return events;
    }

    private static async Task<SseEvent?> ReadEventAsync(StreamReader reader)
    {
        string? name = null, data = null;
        while (await reader.ReadLineAsync() is { } line)
        {
            if (line.StartsWith("event: ", StringComparison.Ordinal)) name = line[7..];
            else if (line.StartsWith("data: ", StringComparison.Ordinal)) data = line[6..];
            else if (line.Length == 0 && name is not null) return new SseEvent(name, JsonDocument.Parse(data!).RootElement);
        }
        return null;
    }

    private Task<List<Message>> MessagesAsync(int conversationId) =>
        factory.WithDbAsync(db => db.Messages.AsNoTracking().Where(m => m.ConversationId == conversationId).OrderBy(m => m.MessageId).ToListAsync());

    // ---------- Streaming ----------

    [Fact]
    public async Task Reply_streams_in_pieces_then_done_with_the_saved_messages()
    {
        var (client, token, conversationId) = await ConversationAsync();

        var response = await client.SendAsync(StreamRequest(conversationId, token, "tell me about streaming replies"),
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        var events = await ReadAllEventsAsync(response);
        var deltas = events.Where(e => e.Name == "delta").Select(e => e.Data.GetProperty("text").GetString()).ToList();
        Assert.True(deltas.Count > 3, "expected several pieces");
        Assert.Equal("**Echo:** tell me about streaming replies", string.Concat(deltas));

        var done = Assert.Single(events, e => e.Name == "done").Data;
        Assert.Equal("done", events[^1].Name);
        Assert.False(done.GetProperty("stopped").GetBoolean());
        Assert.Equal("Fake Generated Title", done.GetProperty("conversation").GetProperty("title").GetString());
        var assistant = done.GetProperty("assistantMessage");
        Assert.Equal("assistant", assistant.GetProperty("role").GetString());
        Assert.Equal(42, assistant.GetProperty("tokenUsage").GetInt32());

        var saved = await MessagesAsync(conversationId);
        Assert.Equal(["user", "assistant"], saved.Select(m => m.Role.ToString().ToLowerInvariant()));
        Assert.Equal(done.GetProperty("userMessage").GetProperty("messageId").GetInt64(), saved[0].MessageId);
        Assert.Equal(assistant.GetProperty("messageId").GetInt64(), saved[1].MessageId);
        Assert.Equal("**Echo:** tell me about streaming replies", saved[1].Content);
    }

    [Fact]
    public async Task Follow_up_questions_get_the_earlier_streamed_turn_as_context()
    {
        var (client, token, conversationId) = await ConversationAsync();
        await ReadAllEventsAsync(await client.SendAsync(StreamRequest(conversationId, token, "My name is Riya"), HttpCompletionOption.ResponseHeadersRead));
        await ReadAllEventsAsync(await client.SendAsync(StreamRequest(conversationId, token, "What is my name?"), HttpCompletionOption.ResponseHeadersRead));

        var prompt = factory.Ai.LastPrompt!;
        Assert.Contains(prompt, m => m.Role == MessageRole.User && m.Content == "My name is Riya");
        Assert.Contains(prompt, m => m.Role == MessageRole.Assistant && m.Content == "**Echo:** My name is Riya");
        Assert.Equal("What is my name?", prompt[^1].Content);
    }

    [Fact]
    public async Task Failure_before_the_first_piece_is_a_normal_http_error_and_nothing_is_saved()
    {
        var (client, token, conversationId) = await ConversationAsync();
        factory.Ai.FailWith = AiFailureKind.Unavailable;

        var response = await client.SendAsync(StreamRequest(conversationId, token, "hello"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("ai_error", (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("reason").GetString());
        Assert.Empty(await MessagesAsync(conversationId));
    }

    [Fact]
    public async Task Failure_part_way_sends_an_error_event_and_saves_nothing()
    {
        var (client, token, conversationId) = await ConversationAsync();
        factory.Ai.FailMidStream = (2, AiFailureKind.Timeout);

        var events = await ReadAllEventsAsync(await client.SendAsync(StreamRequest(conversationId, token, "a fairly long question here"),
            HttpCompletionOption.ResponseHeadersRead));

        Assert.Equal(["delta", "delta", "error"], events.Select(e => e.Name));
        Assert.Equal(504, events[^1].Data.GetProperty("status").GetInt32());
        Assert.Equal("ai_timeout", events[^1].Data.GetProperty("reason").GetString());
        Assert.Empty(await MessagesAsync(conversationId));
    }

    [Fact]
    public async Task Streams_are_owner_only_validated_and_require_sign_in()
    {
        var (_, _, conversationId) = await ConversationAsync();
        var (other, otherToken, _) = await ConversationAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await other.SendAsync(StreamRequest(conversationId, otherToken, "hi"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.CreateBrowserClient().PostAsJsonAsync($"/api/chat/{conversationId}/messages/stream", new { content = "hi" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await other.SendAsync(StreamRequest(conversationId, otherToken, new string('x', 8001)))).StatusCode);
    }

    // ---------- Stop ----------

    [Fact]
    public async Task Stop_keeps_the_partial_reply_and_ends_with_a_stopped_done_event()
    {
        var (client, token, conversationId) = await ConversationAsync();
        factory.Ai.HoldAfterPieces = 3;

        var response = await client.SendAsync(StreamRequest(conversationId, token, "please write a very long answer"),
            HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        var received = new StringBuilder();
        for (var i = 0; i < 3; i++)
        {
            received.Append((await ReadEventAsync(reader))!.Data.GetProperty("text").GetString());
        }
        await factory.Ai.Holding.Task;

        var stop = await client.SendAsync(Authorized(HttpMethod.Post, $"/api/chat/{conversationId}/messages/stop", token));
        Assert.Equal(HttpStatusCode.NoContent, stop.StatusCode);

        var done = await ReadEventAsync(reader);
        Assert.Equal("done", done!.Name);
        Assert.True(done.Data.GetProperty("stopped").GetBoolean());
        Assert.Equal(received.ToString().Trim(), done.Data.GetProperty("assistantMessage").GetProperty("content").GetString());

        var saved = await MessagesAsync(conversationId);
        Assert.Equal(2, saved.Count);
        Assert.Equal("**Echo:** please write", saved[1].Content);
        Assert.True(saved[1].IsTruncated);
        Assert.False(saved[0].IsTruncated);
        Assert.True(done.Data.GetProperty("assistantMessage").GetProperty("isTruncated").GetBoolean());
        Assert.Equal("please write a very long answer", (await factory.WithDbAsync(db => db.Conversations.SingleAsync(c => c.ConversationId == conversationId))).Title);

        // Nothing is running any more.
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.SendAsync(Authorized(HttpMethod.Post, $"/api/chat/{conversationId}/messages/stop", token))).StatusCode);
    }

    [Fact]
    public async Task Stop_before_any_text_keeps_only_the_question()
    {
        var (client, token, conversationId) = await ConversationAsync();
        factory.Ai.HoldAfterPieces = 0;

        // Headers only arrive with the first event, which here is the "done" sent after Stop: don't await yet.
        var sending = client.SendAsync(StreamRequest(conversationId, token, "question only"), HttpCompletionOption.ResponseHeadersRead);
        await factory.Ai.Holding.Task;
        await client.SendAsync(Authorized(HttpMethod.Post, $"/api/chat/{conversationId}/messages/stop", token));

        var events = await ReadAllEventsAsync(await sending);
        var done = Assert.Single(events).Data;
        Assert.True(done.GetProperty("stopped").GetBoolean());
        Assert.Equal(JsonValueKind.Null, done.GetProperty("assistantMessage").ValueKind);
        Assert.Equal([MessageRole.User], (await MessagesAsync(conversationId)).Select(m => m.Role));
    }

    [Fact]
    public async Task Another_user_cannot_stop_my_reply_and_a_second_stream_waits_its_turn()
    {
        var (client, token, conversationId) = await ConversationAsync();
        var (other, otherToken, _) = await ConversationAsync();
        factory.Ai.HoldAfterPieces = 1;

        var response = await client.SendAsync(StreamRequest(conversationId, token, "first"), HttpCompletionOption.ResponseHeadersRead);
        var readAll = ReadAllEventsAsync(response);
        await factory.Ai.Holding.Task;

        Assert.Equal(HttpStatusCode.NotFound,
            (await other.SendAsync(Authorized(HttpMethod.Post, $"/api/chat/{conversationId}/messages/stop", otherToken))).StatusCode);
        var second = await client.SendAsync(StreamRequest(conversationId, token, "second"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("generation_in_progress", (await second.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("reason").GetString());

        await client.SendAsync(Authorized(HttpMethod.Post, $"/api/chat/{conversationId}/messages/stop", token));
        Assert.True((await readAll)[^1].Data.GetProperty("stopped").GetBoolean());
    }

    // ---------- Regenerate ----------

    [Fact]
    public async Task Regenerate_adds_a_new_version_of_the_last_reply_and_keeps_the_old_one()
    {
        var (client, token, conversationId) = await ConversationAsync();
        await ReadAllEventsAsync(await client.SendAsync(StreamRequest(conversationId, token, "first question"), HttpCompletionOption.ResponseHeadersRead));
        await ReadAllEventsAsync(await client.SendAsync(StreamRequest(conversationId, token, "second question"), HttpCompletionOption.ResponseHeadersRead));
        var before = await MessagesAsync(conversationId);

        var events = await ReadAllEventsAsync(await client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/chat/{conversationId}/messages/regenerate", token), HttpCompletionOption.ResponseHeadersRead));

        var done = events[^1].Data;
        Assert.Equal("done", events[^1].Name);
        Assert.Equal(before[3].MessageId, done.GetProperty("replacedMessageId").GetInt64());
        Assert.Equal(before[2].MessageId, done.GetProperty("userMessage").GetProperty("messageId").GetInt64());
        var reply = done.GetProperty("assistantMessage");
        Assert.Equal(2, reply.GetProperty("versionNumber").GetInt32());
        Assert.Equal(2, reply.GetProperty("versionCount").GetInt32());

        // The old reply is kept as version 1 (inactive); the new one is version 2 (active), same parent.
        var after = await MessagesAsync(conversationId);
        Assert.Equal(5, after.Count);
        Assert.Equal(before.Select(m => m.MessageId), after.Take(4).Select(m => m.MessageId));
        Assert.False(after[3].IsActive);
        Assert.True(after[4].IsActive);
        Assert.Equal(after[2].MessageId, after[4].ParentMessageId);
        Assert.Equal((1, 2), (after[3].VersionIndex, after[4].VersionIndex));
        Assert.Equal("**Echo:** second question", after[4].Content);

        // The regenerated answer saw the same context: earlier turn + the same question, without the old reply.
        var prompt = factory.Ai.LastPrompt!;
        Assert.Equal("second question", prompt[^1].Content);
        Assert.Equal(3, prompt.Count(m => m.Role != MessageRole.System));
    }

    [Fact]
    public async Task Regenerate_answers_a_question_whose_reply_was_stopped_before_any_text()
    {
        var (client, token, conversationId) = await ConversationAsync();
        factory.Ai.HoldAfterPieces = 0;
        var sending = client.SendAsync(StreamRequest(conversationId, token, "unanswered"), HttpCompletionOption.ResponseHeadersRead);
        await factory.Ai.Holding.Task;
        await client.SendAsync(Authorized(HttpMethod.Post, $"/api/chat/{conversationId}/messages/stop", token));
        await ReadAllEventsAsync(await sending);
        factory.Ai.ResetStreaming();

        var events = await ReadAllEventsAsync(await client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/chat/{conversationId}/messages/regenerate", token), HttpCompletionOption.ResponseHeadersRead));

        Assert.Equal(JsonValueKind.Null, events[^1].Data.GetProperty("replacedMessageId").ValueKind);
        Assert.Equal(["user", "assistant"], (await MessagesAsync(conversationId)).Select(m => m.Role.ToString().ToLowerInvariant()));
    }

    [Fact]
    public async Task Regenerate_failure_keeps_the_old_reply_and_empty_chats_cannot_regenerate()
    {
        var (client, token, conversationId) = await ConversationAsync();

        var empty = await client.SendAsync(Authorized(HttpMethod.Post, $"/api/chat/{conversationId}/messages/regenerate", token));
        Assert.Equal(HttpStatusCode.Conflict, empty.StatusCode);
        Assert.Equal("nothing_to_regenerate", (await empty.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("reason").GetString());

        await ReadAllEventsAsync(await client.SendAsync(StreamRequest(conversationId, token, "keep me"), HttpCompletionOption.ResponseHeadersRead));
        var before = await MessagesAsync(conversationId);
        factory.Ai.FailWith = AiFailureKind.RateLimited;

        var failed = await client.SendAsync(Authorized(HttpMethod.Post, $"/api/chat/{conversationId}/messages/regenerate", token));

        Assert.Equal(HttpStatusCode.TooManyRequests, failed.StatusCode);
        Assert.Equal(before.Select(m => (m.MessageId, m.Content)), (await MessagesAsync(conversationId)).Select(m => (m.MessageId, m.Content)));
    }

    [Fact]
    public async Task Regenerate_is_owner_only()
    {
        var (client, token, conversationId) = await ConversationAsync();
        await ReadAllEventsAsync(await client.SendAsync(StreamRequest(conversationId, token, "mine"), HttpCompletionOption.ResponseHeadersRead));
        var (other, otherToken, _) = await ConversationAsync();

        var response = await other.SendAsync(Authorized(HttpMethod.Post, $"/api/chat/{conversationId}/messages/regenerate", otherToken));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(2, (await MessagesAsync(conversationId)).Count);
    }
}
