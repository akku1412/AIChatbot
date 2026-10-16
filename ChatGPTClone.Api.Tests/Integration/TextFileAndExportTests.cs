using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static ChatGPTClone.Api.Tests.Integration.TestAuth;

namespace ChatGPTClone.Api.Tests.Integration;

/// <summary>.txt uploads used in chat, and exporting a whole conversation as a .txt download.</summary>
[Collection(ApiCollection.Name)]
public class TextFileAndExportTests(ApiFactory factory)
{
    private async Task<(HttpClient Client, string Token)> SignedInAsync()
    {
        var client = factory.CreateBrowserClient();
        return (client, await SignInAsync(factory, client));
    }

    private static async Task<int> NewConversationAsync(HttpClient client, string token) =>
        (await (await client.SendAsync(Authorized(HttpMethod.Post, "/api/conversations", token, new { })))
            .Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("conversationId").GetInt32();

    [Fact]
    public async Task Txt_upload_is_processed_and_its_text_reaches_the_ai()
    {
        var (client, token) = await SignedInAsync();
        var notes = "Team offsite notes\r\nVenue: Lonavala, Hotel Green Valley\r\nDate: 12 December 2026\r\nBudget per person: 4500 rupees";

        var upload = await UploadAsync(client, token, "offsite notes.txt", Encoding.UTF8.GetBytes(notes));
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var file = await upload.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("completed", file.GetProperty("status").GetString());
        Assert.Equal("text/plain", file.GetProperty("contentType").GetString());
        Assert.Equal(".txt", file.GetProperty("fileExtension").GetString());

        var fileId = file.GetProperty("fileId").GetGuid();
        var chunk = await factory.WithDbAsync(db => db.DocumentChunks.SingleAsync(c => c.FileId == fileId));
        Assert.Equal("Lines 1-4", chunk.Location);
        Assert.Contains("Venue: Lonavala, Hotel Green Valley\nDate: 12 December 2026", chunk.Content);

        var conversation = await NewConversationAsync(client, token);
        var send = await client.SendAsync(Authorized(HttpMethod.Post, $"/api/chat/{conversation}/messages", token,
            new { content = "Where is the offsite and what is the budget?", attachmentIds = new[] { fileId } }));
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);

        var system = factory.Ai.LastPrompt![0].Content;
        Assert.Contains("offsite notes.txt", system);
        Assert.Contains("Lines 1-4", system);
        Assert.Contains("Budget per person: 4500 rupees", system);
    }

    [Fact]
    public async Task Limits_list_txt_as_supported()
    {
        var (client, token) = await SignedInAsync();
        var limits = await (await client.SendAsync(Authorized(HttpMethod.Get, "/api/files/limits", token)))
            .Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Contains(".txt", limits.GetProperty("allowedExtensions").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Export_downloads_the_whole_conversation_as_readable_utf8_text()
    {
        var (client, token) = await SignedInAsync();
        var fileId = (await (await UploadAsync(client, token, "notes.txt", "hello"u8.ToArray()))
            .Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("fileId").GetGuid();
        var conversation = await NewConversationAsync(client, token);
        await client.SendAsync(Authorized(HttpMethod.Post, $"/api/chat/{conversation}/messages", token,
            new { content = "First line\nSecond line — नमस्ते", attachmentIds = new[] { fileId } }));
        await client.SendAsync(Authorized(HttpMethod.Post, $"/api/chat/{conversation}/messages", token,
            new { content = "Follow-up question" }));
        await client.SendAsync(Authorized(HttpMethod.Put, $"/api/conversations/{conversation}", token,
            new { title = "Trip / Plan: Goa?" }));

        var response = await client.SendAsync(Authorized(HttpMethod.Get, $"/api/conversations/{conversation}/export", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType.CharSet);
        var disposition = response.Content.Headers.ContentDisposition!;
        Assert.Equal("attachment", disposition.DispositionType);
        Assert.StartsWith("AIChat - Trip Plan Goa - ", disposition.FileNameStar);
        Assert.EndsWith(".txt", disposition.FileNameStar);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes[3..]);
        Assert.StartsWith("Conversation: Trip / Plan: Goa?\r\n", text);
        Assert.Contains("Messages: 4\r\n", text);
        Assert.Contains("[Attached: notes.txt]\r\nFirst line\r\nSecond line — नमस्ते\r\n", text);
        Assert.Contains("**Echo:**", text); // the AI reply, Markdown kept as readable text
        Assert.Contains("Follow-up question", text);
        Assert.True(text.IndexOf("First line", StringComparison.Ordinal) < text.IndexOf("Follow-up question", StringComparison.Ordinal));
        Assert.Equal(2, CountOf(text, "\r\nYou ("));
        Assert.Equal(2, CountOf(text, "\r\nAIChat ("));
        Assert.DoesNotContain("\n", text.Replace("\r\n", "")); // only CRLF line breaks
    }

    [Fact]
    public async Task Export_is_owner_only_and_requires_sign_in()
    {
        var (owner, ownerToken) = await SignedInAsync();
        var conversation = await NewConversationAsync(owner, ownerToken);
        var (attacker, attackerToken) = await SignedInAsync();

        var foreign = await attacker.SendAsync(Authorized(HttpMethod.Get, $"/api/conversations/{conversation}/export", attackerToken));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        var anonymous = await factory.CreateBrowserClient().GetAsync($"/api/conversations/{conversation}/export");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var missing = await owner.SendAsync(Authorized(HttpMethod.Get, "/api/conversations/999999999/export", ownerToken));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + 1, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }
}
