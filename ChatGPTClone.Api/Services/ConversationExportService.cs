using System.Text;
using System.Text.RegularExpressions;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Models;
using ChatGPTClone.Api.Services.Export;

namespace ChatGPTClone.Api.Services;

/// <summary>A downloadable file: name for Content-Disposition plus its bytes.</summary>
public sealed record ExportedFile(string FileName, string ContentType, byte[] Content);

public interface IConversationExportService
{
    /// <summary>The whole conversation as readable UTF-8 text. 404 when it isn't the user's.</summary>
    Task<ExportedFile> ExportAsTextAsync(int userId, int conversationId, CancellationToken cancellationToken,
        UserTimeZone? timeZone = null);

    /// <summary>
    /// The whole conversation (oldest first, "You"/"AIChat" headings) as TXT, PDF or DOCX. 404 when it isn't the
    /// user's; XLSX is for single replies with tables, so it isn't offered for whole chats. Times are written in
    /// <paramref name="timeZone"/> (UTC when null).
    /// </summary>
    Task<ExportedFile> ExportAsync(int userId, int conversationId, ExportFormat format, CancellationToken cancellationToken,
        UserTimeZone? timeZone = null);
}

public sealed partial class ConversationExportService(
    IConversationService conversations,
    IMessageRepository messages,
    TimeProvider clock,
    ILogger<ConversationExportService> logger) : IConversationExportService
{
    private const string Rule = "------------------------------------------------------------";

    public async Task<ExportedFile> ExportAsync(int userId, int conversationId, ExportFormat format, CancellationToken cancellationToken,
        UserTimeZone? timeZone = null)
    {
        var zone = timeZone ?? UserTimeZone.Utc;
        if (format == ExportFormat.Txt)
        {
            return await ExportAsTextAsync(userId, conversationId, cancellationToken, zone);
        }
        if (format is not (ExportFormat.Pdf or ExportFormat.Docx))
        {
            throw new Middleware.ApiException(StatusCodes.Status400BadRequest, "Unsupported format",
                "Whole chats can be exported as txt, pdf or docx.", new Dictionary<string, object?> { ["reason"] = "unsupported_format" });
        }

        var (conversation, shown) = await LoadThreadAsync(userId, conversationId, cancellationToken);
        var exportedAt = clock.GetUtcNow().UtcDateTime;
        var blocks = ConversationBlocks(shown, zone);
        byte[] content;
        try
        {
            var context = new ExportContext(conversation.Title, exportedAt, zone);
            content = format == ExportFormat.Pdf ? PdfRenderer.Render(blocks, context) : DocxRenderer.Render(blocks, context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Generating a {Format} export of conversation {ConversationId} failed", format, conversationId);
            throw new Middleware.ApiException(StatusCodes.Status500InternalServerError, "Export failed",
                "We couldn't create this file. Please try again or choose another format.",
                new Dictionary<string, object?> { ["reason"] = "export_failed" });
        }

        var fileName = $"AIChat - {SafeFileName(conversation.Title)} - {zone.ToLocal(exportedAt):yyyy-MM-dd}{format.Extension()}";
        return new ExportedFile(fileName, format.ContentType(), content);
    }

    /// <summary>
    /// The chat as one document for the PDF/DOCX renderers: per message a "You"/"AIChat" heading with its time, attached
    /// file names, then the text (AI replies as Markdown with readable citations; user text as typed), separated by rules.
    /// </summary>
    internal static List<DocBlock> ConversationBlocks(IReadOnlyList<Message> shown, UserTimeZone zone)
    {
        var blocks = new List<DocBlock>();
        foreach (var message in shown)
        {
            if (blocks.Count > 0)
            {
                blocks.Add(new RuleBlock());
            }
            var who = message.Role == MessageRole.User ? "You" : "AIChat";
            blocks.Add(new HeadingBlock(2, [new TextRun(who), new TextRun($" · {zone.Format(message.CreatedAt)}", Italic: true)]));

            var attached = message.Attachments.Where(a => a.File is not null).Select(a => a.File.OriginalFileName).ToList();
            if (attached.Count > 0)
            {
                blocks.Add(new ParagraphBlock([new TextRun($"Attached: {string.Join(", ", attached)}", Italic: true)]));
            }

            if (message.Role == MessageRole.Assistant)
            {
                blocks.AddRange(MarkdownReader.Parse(Files.Citations.Readable(message)));
                if (message.IsTruncated)
                {
                    blocks.Add(new ParagraphBlock([new TextRun("(stopped)", Italic: true)]));
                }
            }
            else
            {
                // The user's text exactly as typed (no Markdown), one paragraph per line.
                blocks.AddRange(NormalizeLineBreaks(message.Content).Split("\r\n")
                    .Where(line => line.Trim().Length > 0)
                    .Select(line => new ParagraphBlock([new TextRun(line)])));
            }
        }
        if (blocks.Count == 0)
        {
            blocks.Add(new ParagraphBlock([new TextRun("This conversation has no messages yet.", Italic: true)]));
        }
        return blocks;
    }

    /// <summary>The thread as the user sees it (active versions only), user and AI messages, oldest first. 404 if not theirs.</summary>
    private async Task<(Conversation Conversation, List<Message> Shown)> LoadThreadAsync(int userId, int conversationId,
        CancellationToken cancellationToken)
    {
        // Ownership first: someone else's id is a 404, exactly like the other conversation endpoints.
        var conversation = await conversations.GetOwnedOrThrowAsync(userId, conversationId, cancellationToken);
        var nodes = await messages.GetNodesAsync(conversationId, cancellationToken);
        var thread = ConversationTree.ActiveThread(nodes).Select(t => t.Node.MessageId).ToList();
        var all = await messages.GetByIdsAsync(thread, cancellationToken);
        return (conversation, all.Where(m => m.Role is MessageRole.User or MessageRole.Assistant).ToList());
    }

    public async Task<ExportedFile> ExportAsTextAsync(int userId, int conversationId, CancellationToken cancellationToken,
        UserTimeZone? timeZone = null)
    {
        var zone = timeZone ?? UserTimeZone.Utc;
        var (conversation, shown) = await LoadThreadAsync(userId, conversationId, cancellationToken);
        var exportedAt = clock.GetUtcNow().UtcDateTime;

        // CRLF so the file reads correctly in every editor, including old Notepad.
        var text = new StringBuilder();
        text.Append("Conversation: ").Append(conversation.Title).Append("\r\n");
        text.Append("Exported: ").Append(zone.Format(exportedAt)).Append("\r\n");
        text.Append("Time zone: ").Append(zone.Describe(exportedAt)).Append("\r\n");
        text.Append("Messages: ").Append(shown.Count).Append("\r\n");
        text.Append(Rule.Replace('-', '=')).Append("\r\n");

        foreach (var message in shown)
        {
            text.Append("\r\n");
            text.Append(message.Role == MessageRole.User ? "You" : "AIChat")
                .Append(" (").Append(zone.Format(message.CreatedAt)).Append("):\r\n");

            var attached = message.Attachments.Where(a => a.File is not null).Select(a => a.File.OriginalFileName).ToList();
            if (attached.Count > 0)
            {
                text.Append("[Attached: ").Append(string.Join(", ", attached)).Append("]\r\n");
            }
            if (message.Content.Length > 0)
            {
                // AI replies are Markdown: write them as clean plain text (no ** or #). User text is kept as typed.
                var body = message.Role == MessageRole.Assistant
                    ? PlainTextRenderer.Render(MarkdownReader.Parse(Files.Citations.Readable(message)))
                    : NormalizeLineBreaks(message.Content);
                text.Append(body.TrimEnd()).Append("\r\n");
            }
            text.Append("\r\n").Append(Rule).Append("\r\n");
        }

        var fileName = $"AIChat - {SafeFileName(conversation.Title)} - {zone.ToLocal(exportedAt):yyyy-MM-dd}.txt";
        // UTF-8 with a BOM so Windows editors never mistake Hindi or other non-English text for ANSI.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text.ToString())).ToArray();
        return new ExportedFile(fileName, "text/plain; charset=utf-8", bytes);
    }

    private static string NormalizeLineBreaks(string value) =>
        value.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");

    /// <summary>Title → something every OS accepts as a file name (no \ / : * ? " &lt; &gt; |, no control characters).</summary>
    internal static string SafeFileName(string title)
    {
        var cleaned = UnsafeFileNameCharacters().Replace(title, " ");
        cleaned = Whitespace().Replace(cleaned, " ").Trim().Trim('.');
        if (cleaned.Length > 60)
        {
            cleaned = cleaned[..60].TrimEnd();
        }
        return cleaned.Length == 0 ? "Chat" : cleaned;
    }

    [GeneratedRegex(@"[\\/:*?""<>|\p{C}]")]
    private static partial Regex UnsafeFileNameCharacters();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
