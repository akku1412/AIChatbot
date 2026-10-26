using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ChatGPTClone.Api.Models;
using ChatGPTClone.Api.Services;
using Microsoft.EntityFrameworkCore;
using static ChatGPTClone.Api.Tests.Integration.TestAuth;

namespace ChatGPTClone.Api.Tests.Integration;

/// <summary>
/// Roadmap Phase 2 through the real API: background processing, CSV/PPTX, .doc rejection, OCR of scans and images,
/// image uploads (EXIF removed, own size limit, vision routing), hybrid retrieval and clickable citations.
/// </summary>
[Collection(ApiCollection.Name)]
public class DocumentIntelligenceTests(ApiFactory factory)
{
    private async Task<(HttpClient Client, string Token)> SignedInAsync()
    {
        var client = factory.CreateBrowserClient();
        return (client, await SignInAsync(factory, client));
    }

    private static async Task<JsonElement> UploadOkAsync(HttpClient client, string token, string name, byte[] content)
    {
        var response = await UploadAsync(client, token, name, content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var file = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("completed", file.GetProperty("status").GetString());
        return file;
    }

    private static async Task<int> NewConversationAsync(HttpClient client, string token) =>
        (await (await client.SendAsync(Authorized(HttpMethod.Post, "/api/conversations", token, new { })))
            .Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("conversationId").GetInt32();

    private static async Task<JsonElement> SendAsync(HttpClient client, string token, int conversationId, string content, params Guid[] files)
    {
        var response = await client.SendAsync(Authorized(HttpMethod.Post, $"/api/chat/{conversationId}/messages", token,
            new { content, attachmentIds = files }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    private Task<List<DocumentChunk>> ChunksAsync(Guid fileId) =>
        factory.WithDbAsync(db => db.DocumentChunks.AsNoTracking().Where(c => c.FileId == fileId).OrderBy(c => c.ChunkIndex).ToListAsync());

    // ---------- Background processing (feature 8 / 21) ----------

    [Fact]
    public async Task Upload_returns_immediately_as_processing_and_the_file_card_can_poll_until_completed()
    {
        var (client, token) = await SignedInAsync();

        var response = await UploadRawAsync(client, token, "notes.txt", "Background processing works."u8.ToArray());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var accepted = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("processing", accepted.GetProperty("status").GetString());
        var done = await WaitForProcessingAsync(client, token, accepted.GetProperty("fileId").GetGuid());
        Assert.Equal("completed", done.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, done.GetProperty("progressTotal").ValueKind);
    }

    [Fact]
    public async Task A_file_still_processing_cannot_be_attached_yet()
    {
        var (client, token) = await SignedInAsync();
        var conversationId = await NewConversationAsync(client, token);
        // Insert a file that is (still) processing directly, so the outcome doesn't depend on timing.
        var fileId = Guid.NewGuid();
        var me = await (await client.SendAsync(Authorized(HttpMethod.Get, "/api/users/me", token))).Content.ReadFromJsonAsync<JsonElement>(Json);
        await factory.WithDbAsync(async db =>
        {
            db.UploadedFiles.Add(new UploadedFile
            {
                FileId = fileId, UserId = me.GetProperty("userId").GetInt32(), OriginalFileName = "slow.pdf",
                StorageKey = Guid.NewGuid().ToString("N") + ".bin", ContentType = "application/pdf", FileExtension = ".pdf",
                FileSize = 10, ProcessingStatus = FileProcessingStatus.Processing, StatusMessage = "Reading scanned pages",
                ProgressCurrent = 3, ProgressTotal = 12,
            });
            return await db.SaveChangesAsync();
        });

        var card = await (await client.SendAsync(Authorized(HttpMethod.Get, $"/api/files/{fileId}", token))).Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("Reading scanned pages", card.GetProperty("statusMessage").GetString());
        Assert.Equal(3, card.GetProperty("progressCurrent").GetInt32());
        Assert.Equal(12, card.GetProperty("progressTotal").GetInt32());

        var send = await client.SendAsync(Authorized(HttpMethod.Post, $"/api/chat/{conversationId}/messages", token,
            new { content = "Summarize", attachmentIds = new[] { fileId } }));
        Assert.Equal(HttpStatusCode.Conflict, send.StatusCode);
        Assert.Equal("file_not_ready", (await send.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("reason").GetString());
        await factory.WithDbAsync(db => db.UploadedFiles.Where(f => f.FileId == fileId).ExecuteDeleteAsync());
    }

    // ---------- More file types (feature 10) ----------

    [Fact]
    public async Task Csv_is_read_like_a_spreadsheet_with_exact_computed_totals()
    {
        var (client, token) = await SignedInAsync();
        var csv = "Region;Revenue;Date\nNorth;1200,50;2026-01-15\nSouth;800;2026-02-01\nEast;;2026-03-10\nWest;2000;2026-04-20\n";

        var file = await UploadOkAsync(client, token, "sales.csv", Encoding.UTF8.GetBytes(csv));

        Assert.Equal("text/csv", file.GetProperty("contentType").GetString());
        var chunks = await ChunksAsync(file.GetProperty("fileId").GetGuid());
        var summary = Assert.Single(chunks, c => c.Kind == DocumentChunkKind.Summary).Content;
        Assert.Contains("Revenue", summary);
        Assert.Contains("1 blank", summary);
        Assert.Contains(chunks, c => c.Kind == DocumentChunkKind.Text && c.Content.Contains("South"));
    }

    [Fact]
    public async Task Pptx_slides_and_speaker_notes_are_read_and_cited_as_slide_numbers()
    {
        var (client, token) = await SignedInAsync();
        var pptx = TestDocuments.Pptx(
            ("Quarterly review", ["Revenue up 12%"], "Mention the new office"),
            ("Next steps", ["Hire two engineers", "Launch in March"], null));

        var file = await UploadOkAsync(client, token, "review.pptx", pptx);

        Assert.Equal(2, file.GetProperty("pageCount").GetInt32());
        var chunks = await ChunksAsync(file.GetProperty("fileId").GetGuid());
        Assert.Equal(["Slide 1", "Slide 2"], chunks.Select(c => c.Location));
        Assert.Contains("Speaker notes:", chunks[0].Content);
        Assert.Contains("Mention the new office", chunks[0].Content);
        Assert.Contains("Launch in March", chunks[1].Content);
    }

    [Fact]
    public async Task Old_word_doc_files_are_rejected_with_save_as_docx_advice()
    {
        var (client, token) = await SignedInAsync();
        // Word 97–2003 files start with the OLE compound-file signature.
        byte[] doc = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, .. new byte[500]];

        var response = await UploadAsync(client, token, "old.doc", doc);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("doc_not_supported", problem.GetProperty("reason").GetString());
        Assert.Contains(".docx", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Limits_list_the_new_types_and_the_image_limit()
    {
        var (client, token) = await SignedInAsync();
        var limits = await (await client.SendAsync(Authorized(HttpMethod.Get, "/api/files/limits", token))).Content.ReadFromJsonAsync<JsonElement>(Json);
        var allowed = limits.GetProperty("allowedExtensions").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains(".csv", allowed);
        Assert.Contains(".pptx", allowed);
        Assert.Contains(".png", allowed);
        Assert.DoesNotContain(".doc", allowed);
        Assert.Equal(ApiFactory.MaxImageSize, limits.GetProperty("maxImageSizeBytes").GetInt64());
    }

    // ---------- OCR (feature 8) ----------

    [Fact]
    public async Task Scanned_pdf_pages_are_read_with_ocr_and_marked_as_ocr()
    {
        var (client, token) = await SignedInAsync();
        var scan = TestDocuments.ScannedPdf(["INVOICE NUMBER 4417", "TOTAL DUE 9250 RUPEES"], ["PAYMENT TERMS THIRTY DAYS"]);

        var file = await UploadOkAsync(client, token, "scan.pdf", scan);

        var chunks = await ChunksAsync(file.GetProperty("fileId").GetGuid());
        Assert.All(chunks, c => Assert.Equal(ChunkSource.Ocr, c.Source));
        var text = string.Join("\n", chunks.Select(c => c.Content)).ToUpperInvariant();
        Assert.Contains("4417", text);
        Assert.Contains("9250", text);
        Assert.Contains("THIRTY", text);
        Assert.Equal(["Page 1", "Page 2"], chunks.Select(c => c.Location));
    }

    // ---------- Images and vision (feature 9) ----------

    [Fact]
    public async Task Images_are_stored_without_exif_their_text_is_ocrd_and_they_go_to_the_ai_as_image_parts()
    {
        var (client, token) = await SignedInAsync();
        var jpeg = TestDocuments.JpegWithExif("SECRET-GPS-28.61N-77.20E");
        Assert.Contains("SECRET-GPS", Encoding.ASCII.GetString(jpeg)); // the test file really carries it

        var photo = await UploadOkAsync(client, token, "photo.jpg", jpeg);
        Assert.True(photo.GetProperty("isImage").GetBoolean());
        var download = await client.SendAsync(Authorized(HttpMethod.Get, $"/api/files/{photo.GetProperty("fileId").GetGuid()}/download", token));
        var stored = await download.Content.ReadAsByteArrayAsync();
        Assert.Equal(0xFF, stored[0]); // still a JPEG
        Assert.DoesNotContain("SECRET-GPS", Encoding.ASCII.GetString(stored));
        Assert.DoesNotContain("Exif", Encoding.ASCII.GetString(stored));

        var screenshot = await UploadOkAsync(client, token, "error.png", TestDocuments.TextPng(["ERROR CODE 5021", "DISK QUOTA EXCEEDED"]));
        var chunks = await ChunksAsync(screenshot.GetProperty("fileId").GetGuid());
        Assert.Contains("5021", string.Join(" ", chunks.Select(c => c.Content)));
        Assert.All(chunks, c => Assert.Equal(ChunkSource.Ocr, c.Source));

        var conversationId = await NewConversationAsync(client, token);
        var body = await SendAsync(client, token, conversationId, "What does this error mean?", screenshot.GetProperty("fileId").GetGuid());

        var prompt = factory.Ai.LastPrompt!;
        var question = prompt[^1];
        var image = Assert.Single(question.Images!);
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal(0x89, image.Data[0]); // PNG bytes
        Assert.Contains(ChatService.ImageInstructions, prompt[0].Content);
        Assert.All(prompt.SkipLast(1), m => Assert.Null(m.Images)); // only the question carries images
        var attachment = body.GetProperty("userMessage").GetProperty("attachments")[0];
        Assert.True(attachment.GetProperty("isImage").GetBoolean());
    }

    [Fact]
    public async Task Images_have_their_own_size_limit_and_fake_images_are_rejected()
    {
        var (client, token) = await SignedInAsync();

        var big = TestDocuments.NoisePng(360, 360);
        Assert.InRange(big.Length, ApiFactory.MaxImageSize + 1, ApiFactory.MaxFileSize - 1);
        var tooBig = await UploadAsync(client, token, "big.png", big);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooBig.StatusCode);
        Assert.Equal("too_large", (await tooBig.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("reason").GetString());

        var fake = await UploadAsync(client, token, "cat.png", TestDocuments.FakeExe());
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, fake.StatusCode);
        Assert.Equal("invalid_format", (await fake.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("reason").GetString());

        var jpegNamedPng = await UploadAsync(client, token, "photo.png", TestDocuments.JpegWithExif("x"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, jpegNamedPng.StatusCode);
    }

    // ---------- Hybrid retrieval (feature 7) ----------

    /// <summary>~40 filler sections and one about revenue near the end, far beyond the context budget.</summary>
    private static byte[] LongReport()
    {
        var text = new StringBuilder();
        for (var i = 1; i <= 40; i++)
        {
            text.Append($"Section {i}. ");
            text.Append(i == 37
                ? "Revenue grew 12% compared with last year, mostly from the new northern region. "
                : $"This part describes office logistics, parking rules and cafeteria menus for building {i}. ");
            text.AppendLine(string.Concat(Enumerable.Repeat($"Routine detail number {i} about facilities. ", 45)));
            text.AppendLine();
        }
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    [Fact]
    public async Task Semantic_search_finds_a_passage_that_shares_no_keyword_with_the_question()
    {
        var (client, token) = await SignedInAsync();
        var file = await UploadOkAsync(client, token, "report.txt", LongReport());
        var fileId = file.GetProperty("fileId").GetGuid();
        var indexed = await factory.WithDbAsync(db => db.ChunkEmbeddings.CountAsync(e => e.Chunk.FileId == fileId));
        Assert.True(indexed > 10, $"expected the chunks to be indexed, got {indexed}");
        var conversationId = await NewConversationAsync(client, token);

        try
        {
            // Keyword-only (semantic search unavailable): "sales" and "change" appear nowhere in the report.
            factory.Embeddings.IsAvailable = false;
            await SendAsync(client, token, conversationId, "How did sales change?", fileId);
            Assert.DoesNotContain("Revenue grew 12%", factory.Ai.LastPrompt![0].Content);

            // Hybrid: the question's meaning matches the revenue section.
            factory.Embeddings.IsAvailable = true;
            await SendAsync(client, token, conversationId, "How did sales change?");
            Assert.Contains("Revenue grew 12%", factory.Ai.LastPrompt![0].Content);

            // A failing embedding model never breaks the chat: it falls back to keywords.
            factory.Embeddings.Fail = true;
            await SendAsync(client, token, conversationId, "What are the parking rules?");
            Assert.Contains("parking rules", factory.Ai.LastPrompt![0].Content);
        }
        finally
        {
            factory.Embeddings.IsAvailable = true;
            factory.Embeddings.Fail = false;
        }
    }

    // ---------- Citations (feature 11) ----------

    [Fact]
    public async Task Cited_passages_become_sources_with_an_owner_only_preview_and_readable_exports()
    {
        var (owner, ownerToken) = await SignedInAsync();
        var (other, otherToken) = await SignedInAsync();
        var file = await UploadOkAsync(owner, ownerToken, "memo.pdf", TestDocuments.Pdf(["Intro text"], ["The deadline is 2026-12-15."]));
        var fileId = file.GetProperty("fileId").GetGuid();
        var pageTwo = (await ChunksAsync(fileId)).Single(c => c.Location == "Page 2").ChunkId;
        var otherFile = await UploadOkAsync(other, otherToken, "other.txt", "Someone else's secret"u8.ToArray());
        var foreignChunk = (await ChunksAsync(otherFile.GetProperty("fileId").GetGuid())).First().ChunkId;
        var conversationId = await NewConversationAsync(owner, ownerToken);

        try
        {
            // The AI cites a real passage, an invented id and another user's passage; only the real one counts.
            factory.Ai.Reply = _ => $"The deadline is 15 December 2026 [file:{pageTwo}]. Also see [file:99999999] and [file:{foreignChunk}].";
            var body = await SendAsync(owner, ownerToken, conversationId, "When is the deadline?", fileId);

            var reply = body.GetProperty("assistantMessage");
            var source = Assert.Single(reply.GetProperty("sources").EnumerateArray());
            Assert.Equal(pageTwo, source.GetProperty("chunkId").GetInt64());
            Assert.Equal("memo.pdf", source.GetProperty("fileName").GetString());
            Assert.Equal("Page 2", source.GetProperty("location").GetString());
            Assert.False(source.GetProperty("isOcr").GetBoolean());
            var messageId = reply.GetProperty("messageId").GetInt64();

            // Sources come back with the conversation too.
            var page = await (await owner.SendAsync(Authorized(HttpMethod.Get, $"/api/chat/{conversationId}/messages", ownerToken)))
                .Content.ReadFromJsonAsync<JsonElement>(Json);
            var saved = page.GetProperty("items").EnumerateArray().Single(m => m.GetProperty("messageId").GetInt64() == messageId);
            Assert.Equal(pageTwo, Assert.Single(saved.GetProperty("sources").EnumerateArray()).GetProperty("chunkId").GetInt64());
        }
        finally
        {
            factory.Ai.Reply = null;
        }

        // Preview panel: owner only; another user, another file's id, or a missing chunk are all 404.
        var preview = await owner.SendAsync(Authorized(HttpMethod.Get, $"/api/files/{fileId}/chunks/{pageTwo}", ownerToken));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        // Compare flags, not ToString(): the parsed header re-orders directives ("no-store, private").
        Assert.True(preview.Headers.CacheControl!.Private);
        Assert.True(preview.Headers.CacheControl.NoStore);
        var chunk = await preview.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Contains("2026-12-15", chunk.GetProperty("content").GetString());
        Assert.Equal("text", chunk.GetProperty("source").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await other.SendAsync(Authorized(HttpMethod.Get, $"/api/files/{fileId}/chunks/{pageTwo}", otherToken))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.SendAsync(Authorized(HttpMethod.Get,
            $"/api/files/{otherFile.GetProperty("fileId").GetGuid()}/chunks/{pageTwo}", ownerToken))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.SendAsync(Authorized(HttpMethod.Get, $"/api/files/{fileId}/chunks/{foreignChunk}", ownerToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"/api/files/{fileId}/chunks/{pageTwo}")).StatusCode);

        // Exports turn markers into "(file, location)" and drop unknown ones.
        var export = await owner.SendAsync(Authorized(HttpMethod.Get, $"/api/conversations/{conversationId}/export", ownerToken));
        var text = await export.Content.ReadAsStringAsync();
        Assert.Contains("(memo.pdf, Page 2)", text);
        Assert.DoesNotContain("[file:", text);

        // Deleting the file removes its sources; the reply text stays.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.SendAsync(Authorized(HttpMethod.Delete, $"/api/files/{fileId}", ownerToken))).StatusCode);
        Assert.Equal(0, await factory.WithDbAsync(db => db.MessageSources.CountAsync(s => s.ChunkId == pageTwo)));
    }
}
