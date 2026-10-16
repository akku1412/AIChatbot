using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ChatGPTClone.Api.Models;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;
using static ChatGPTClone.Api.Tests.Integration.TestAuth;

namespace ChatGPTClone.Api.Tests.Integration;

/// <summary>Original-file downloads and AI-reply exports through the real API, including ownership and auth.</summary>
[Collection(ApiCollection.Name)]
public class DownloadAndExportTests(ApiFactory factory)
{
    private const string TableReply = """
        ## Regional sales

        | Region | Revenue |
        |---|---:|
        | North | 1,200 |
        | West | 1,999.5 |

        **Total:** 3,199.5
        """;

    private async Task<(HttpClient Client, string Token)> SignedInAsync()
    {
        var client = factory.CreateBrowserClient();
        return (client, await SignInAsync(factory, client));
    }

    private static async Task<Guid> UploadOkAsync(HttpClient client, string token, string name, byte[] content)
    {
        var response = await UploadAsync(client, token, name, content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("fileId").GetGuid();
    }

    /// <summary>A conversation owned by the token's user with one assistant message whose Markdown we control.</summary>
    private async Task<long> AssistantMessageAsync(HttpClient client, string token, string markdown, string title = "Sales review")
    {
        var created = await (await client.SendAsync(Authorized(HttpMethod.Post, "/api/conversations", token, new { title })))
            .Content.ReadFromJsonAsync<JsonElement>(Json);
        var conversationId = created.GetProperty("conversationId").GetInt32();
        return await factory.WithDbAsync(async db =>
        {
            var message = new Message { ConversationId = conversationId, Role = MessageRole.Assistant, Content = markdown };
            db.Messages.Add(message);
            await db.SaveChangesAsync();
            return message.MessageId;
        });
    }

    private static Task<HttpResponseMessage> ExportAsync(HttpClient client, string token, long messageId, string format) =>
        client.SendAsync(Authorized(HttpMethod.Get, $"/api/messages/{messageId}/export?format={format}", token));

    // ---------- Original file downloads ----------

    public static TheoryData<string, string> Originals => new()
    {
        { "notes.txt", "text/plain" },
        { "report.pdf", "application/pdf" },
        { "plan.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        { "sales.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
    };

    [Theory]
    [MemberData(nameof(Originals))]
    public async Task Owner_downloads_the_original_bytes_with_name_and_content_type(string name, string contentType)
    {
        var (client, token) = await SignedInAsync();
        var bytes = Path.GetExtension(name) switch
        {
            ".txt" => Encoding.UTF8.GetBytes("Line one\r\nनमस्ते\r\n"),
            ".pdf" => TestDocuments.Pdf(["Page one"]),
            ".docx" => TestDocuments.Docx("Plan", ["Ship in Q4."]),
            _ => TestDocuments.SalesXlsx(),
        };
        var fileId = await UploadOkAsync(client, token, name, bytes);

        var response = await client.SendAsync(Authorized(HttpMethod.Get, $"/api/files/{fileId}/download", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal(name, response.Content.Headers.ContentDisposition.FileNameStar);
        Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Downloads_are_owner_only_and_require_sign_in()
    {
        var (owner, ownerToken) = await SignedInAsync();
        var fileId = await UploadOkAsync(owner, ownerToken, "secret.txt", "top secret"u8.ToArray());
        var (attacker, attackerToken) = await SignedInAsync();

        var foreign = await attacker.SendAsync(Authorized(HttpMethod.Get, $"/api/files/{fileId}/download", attackerToken));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.DoesNotContain("top secret", await foreign.Content.ReadAsStringAsync());

        var anonymous = await factory.CreateBrowserClient().GetAsync($"/api/files/{fileId}/download");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var missing = await owner.SendAsync(Authorized(HttpMethod.Get, $"/api/files/{Guid.NewGuid()}/download", ownerToken));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var problem = await missing.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("file_not_found", problem.GetProperty("reason").GetString());
        Assert.DoesNotContain(factory.StoragePath, await missing.Content.ReadAsStringAsync());

        var notAGuid = await owner.SendAsync(Authorized(HttpMethod.Get, "/api/files/..%2F..%2Fappsettings.json/download", ownerToken));
        Assert.Equal(HttpStatusCode.NotFound, notAGuid.StatusCode);
    }

    [Fact]
    public async Task Download_of_a_file_whose_stored_copy_vanished_is_a_clean_404()
    {
        var (client, token) = await SignedInAsync();
        var fileId = await UploadOkAsync(client, token, "gone.txt", "bye"u8.ToArray());
        var key = await factory.WithDbAsync(db => Task.FromResult(db.UploadedFiles.Single(f => f.FileId == fileId).StorageKey));
        File.Delete(Path.Combine(factory.StoragePath, key));

        var response = await client.SendAsync(Authorized(HttpMethod.Get, $"/api/files/{fileId}/download", token));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("file_missing", (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("reason").GetString());
    }

    // ---------- AI reply exports ----------

    [Fact]
    public async Task Reply_exports_as_real_txt_pdf_docx_and_xlsx_files()
    {
        var (client, token) = await SignedInAsync();
        var messageId = await AssistantMessageAsync(client, token, TableReply, "Sales / Review: Q1");

        var txt = await ExportAsync(client, token, messageId, "txt");
        Assert.Equal(HttpStatusCode.OK, txt.StatusCode);
        Assert.Equal("text/plain", txt.Content.Headers.ContentType!.MediaType);
        var txtBytes = await txt.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetPreamble(), txtBytes[..3]);
        var plain = Encoding.UTF8.GetString(txtBytes[3..]);
        Assert.Contains("Regional sales\r\n--------------", plain);
        Assert.Contains("Total: 3,199.5", plain);
        Assert.DoesNotContain("**", plain);

        var pdf = await ExportAsync(client, token, messageId, "pdf");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        using (var document = PdfDocument.Open(await pdf.Content.ReadAsByteArrayAsync()))
        {
            Assert.Contains("Regional sales", document.GetPage(1).Text);
        }

        var docx = await ExportAsync(client, token, messageId, "docx");
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", docx.Content.Headers.ContentType!.MediaType);
        using (var word = WordprocessingDocument.Open(new MemoryStream(await docx.Content.ReadAsByteArrayAsync()), false))
        {
            Assert.Contains("North", word.MainDocumentPart!.Document!.Body!.InnerText);
        }

        var xlsx = await ExportAsync(client, token, messageId, "xlsx");
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsx.Content.Headers.ContentType!.MediaType);
        using (var workbook = new XLWorkbook(new MemoryStream(await xlsx.Content.ReadAsByteArrayAsync())))
        {
            Assert.Equal("Regional sales", workbook.Worksheet(1).Name);
            Assert.Equal(1999.5, workbook.Worksheet(1).Cell("B3").GetDouble());
        }

        foreach (var (response, extension) in new[] { (txt, ".txt"), (pdf, ".pdf"), (docx, ".docx"), (xlsx, ".xlsx") })
        {
            var name = response.Content.Headers.ContentDisposition!.FileNameStar!;
            Assert.StartsWith("AIChat reply - Sales Review Q1 - ", name);
            Assert.EndsWith(extension, name);
            Assert.Equal("attachment", response.Content.Headers.ContentDisposition.DispositionType);
        }
    }

    [Fact]
    public async Task Xlsx_export_of_a_reply_without_tables_explains_why()
    {
        var (client, token) = await SignedInAsync();
        var messageId = await AssistantMessageAsync(client, token, "Hello! How can I help you today?");

        var response = await ExportAsync(client, token, messageId, "xlsx");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("no_tabular_data", problem.GetProperty("reason").GetString());
        Assert.Contains("PDF, DOCX or TXT", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Exports_are_owner_only_validate_the_format_and_require_sign_in()
    {
        var (owner, ownerToken) = await SignedInAsync();
        var messageId = await AssistantMessageAsync(owner, ownerToken, TableReply);
        var (attacker, attackerToken) = await SignedInAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await ExportAsync(attacker, attackerToken, messageId, "pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ExportAsync(owner, ownerToken, 999_999_999, "pdf")).StatusCode);

        var badFormat = await ExportAsync(owner, ownerToken, messageId, "exe");
        Assert.Equal(HttpStatusCode.BadRequest, badFormat.StatusCode);
        Assert.Equal("unsupported_format", (await badFormat.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("reason").GetString());

        var anonymous = await factory.CreateBrowserClient().GetAsync($"/api/messages/{messageId}/export?format=pdf");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task A_real_ai_reply_can_be_exported_after_sending_a_message()
    {
        var (client, token) = await SignedInAsync();
        var conversation = (await (await client.SendAsync(Authorized(HttpMethod.Post, "/api/conversations", token, new { })))
            .Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("conversationId").GetInt32();
        var sent = await (await client.SendAsync(Authorized(HttpMethod.Post, $"/api/chat/{conversation}/messages", token,
            new { content = "Summarize our plan" }))).Content.ReadFromJsonAsync<JsonElement>(Json);
        var replyId = sent.GetProperty("assistantMessage").GetProperty("messageId").GetInt64();

        var response = await ExportAsync(client, token, replyId, "docx");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var word = WordprocessingDocument.Open(new MemoryStream(await response.Content.ReadAsByteArrayAsync()), false);
        Assert.Contains("Echo: Summarize our plan", word.MainDocumentPart!.Document!.Body!.InnerText);
    }
}
