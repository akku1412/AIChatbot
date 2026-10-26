using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;
using static ChatGPTClone.Api.Tests.Integration.TestAuth;

namespace ChatGPTClone.Api.Tests.Integration;

/// <summary>Roadmap feature 20: the whole chat as PDF or DOCX (GET /api/conversations/{id}/export?format=…).</summary>
[Collection(ApiCollection.Name)]
public sealed class ConversationExportTests(ApiFactory factory) : IDisposable
{
    public void Dispose() => factory.Ai.Reply = null;

    private sealed record User(HttpClient Client, string Token);

    private async Task<User> SignedInAsync()
    {
        var client = factory.CreateBrowserClient();
        return new User(client, await SignInAsync(factory, client));
    }

    private static Task<HttpResponseMessage> GetAsync(User user, string url) =>
        user.Client.SendAsync(Authorized(HttpMethod.Get, url, user.Token));

    /// <summary>A chat titled "Fake Generated Title" with two exchanges; the replies are Markdown.</summary>
    private async Task<int> ChatAsync(User user)
    {
        var id = (await (await user.Client.SendAsync(Authorized(HttpMethod.Post, "/api/conversations", user.Token, new { })))
            .Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("conversationId").GetInt32();
        factory.Ai.Reply = _ => "## Plan\n\n- **Book** the venue\n- Send invites";
        await user.Client.SendAsync(Authorized(HttpMethod.Post, $"/api/chat/{id}/messages", user.Token, new { content = "Plan a launch party" }));
        factory.Ai.Reply = _ => "Start with *30 guests*.";
        await user.Client.SendAsync(Authorized(HttpMethod.Post, $"/api/chat/{id}/messages", user.Token, new { content = "How many guests?" }));
        return id;
    }

    private static void AssertInOrder(string text, params string[] parts)
    {
        var at = 0;
        foreach (var part in parts)
        {
            var found = text.IndexOf(part, at, StringComparison.Ordinal);
            Assert.True(found >= 0, $"\"{part}\" missing (or out of order) in: {text}");
            at = found + part.Length;
        }
    }

    [Fact]
    public async Task Pdf_has_every_message_in_order_with_you_and_aichat_headings()
    {
        var user = await SignedInAsync();
        var chat = await ChatAsync(user);

        var response = await GetAsync(user, $"/api/conversations/{chat}/export?format=pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        Assert.EndsWith(".pdf", response.Content.Headers.ContentDisposition!.FileNameStar);
        Assert.True(response.Headers.CacheControl!.NoStore);
        using var pdf = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        var text = string.Join(" ", pdf.GetPages().Select(p => p.Text));
        AssertInOrder(text, "You", "Plan a launch party", "AIChat", "Plan", "Book", "Send invites",
            "You", "How many guests?", "AIChat", "30 guests");
        Assert.DoesNotContain("**", text); // Markdown is rendered, not printed
    }

    [Fact]
    public async Task Docx_has_headings_and_formatted_replies()
    {
        var user = await SignedInAsync();
        var chat = await ChatAsync(user);

        var response = await GetAsync(user, $"/api/conversations/{chat}/export?format=docx");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.EndsWith(".docx", response.Content.Headers.ContentDisposition!.FileNameStar);
        using var word = WordprocessingDocument.Open(new MemoryStream(await response.Content.ReadAsByteArrayAsync()), false);
        var paragraphs = word.MainDocumentPart!.Document.Body!.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().ToList();
        var text = string.Join("\n", paragraphs.Select(p => p.InnerText));
        AssertInOrder(text, "You", "Plan a launch party", "AIChat", "Book the venue", "How many guests?", "Start with 30 guests.");

        var headings = paragraphs.Where(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value?.StartsWith("Heading") == true)
            .Select(p => p.InnerText).ToList();
        Assert.Equal(4, headings.Count(h => h.StartsWith("You ·") || h.StartsWith("AIChat ·")));
        var bold = word.MainDocumentPart.Document.Body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Run>()
            .Where(r => r.RunProperties?.Bold is not null).Select(r => r.InnerText);
        Assert.Contains("Book", bold);
    }

    [Fact]
    public async Task Txt_stays_the_default_and_unknown_formats_are_refused()
    {
        var user = await SignedInAsync();
        var chat = await ChatAsync(user);

        var txt = await GetAsync(user, $"/api/conversations/{chat}/export");
        Assert.Equal("text/plain", txt.Content.Headers.ContentType!.MediaType);
        Assert.Contains("Plan a launch party", await txt.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, (await GetAsync(user, $"/api/conversations/{chat}/export?format=xlsx")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await GetAsync(user, $"/api/conversations/{chat}/export?format=html")).StatusCode);
    }

    [Fact]
    public async Task Another_users_chat_cannot_be_exported()
    {
        var owner = await SignedInAsync();
        var other = await SignedInAsync();
        var chat = await ChatAsync(owner);

        foreach (var format in new[] { "pdf", "docx", "txt" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(other, $"/api/conversations/{chat}/export?format={format}")).StatusCode);
        }
    }

    [Fact]
    public async Task An_empty_chat_still_exports()
    {
        var user = await SignedInAsync();
        var id = (await (await user.Client.SendAsync(Authorized(HttpMethod.Post, "/api/conversations", user.Token, new { title = "Empty" })))
            .Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("conversationId").GetInt32();

        var response = await GetAsync(user, $"/api/conversations/{id}/export?format=pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var pdf = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        Assert.Contains("no messages yet", string.Join(" ", pdf.GetPages().Select(p => p.Text)));
    }
}
