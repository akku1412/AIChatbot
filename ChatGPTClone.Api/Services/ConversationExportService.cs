using System.Globalization;
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
    Task<ExportedFile> ExportAsTextAsync(int userId, int conversationId, CancellationToken cancellationToken);
}

public sealed partial class ConversationExportService(
    IConversationService conversations,
    IMessageRepository messages,
    TimeProvider clock) : IConversationExportService
{
    private const string Rule = "------------------------------------------------------------";

    public async Task<ExportedFile> ExportAsTextAsync(int userId, int conversationId, CancellationToken cancellationToken)
    {
        // Ownership first: someone else's id is a 404, exactly like the other conversation endpoints.
        var conversation = await conversations.GetOwnedOrThrowAsync(userId, conversationId, cancellationToken);
        var all = await messages.GetAllAsync(conversationId, cancellationToken);
        var shown = all.Where(m => m.Role is MessageRole.User or MessageRole.Assistant).ToList();
        var exportedAt = clock.GetUtcNow().UtcDateTime;

        // CRLF so the file reads correctly in every editor, including old Notepad.
        var text = new StringBuilder();
        text.Append("Conversation: ").Append(conversation.Title).Append("\r\n");
        text.Append("Exported: ").Append(Format(exportedAt)).Append("\r\n");
        text.Append("Messages: ").Append(shown.Count).Append("\r\n");
        text.Append(Rule.Replace('-', '=')).Append("\r\n");

        foreach (var message in shown)
        {
            text.Append("\r\n");
            text.Append(message.Role == MessageRole.User ? "You" : "AIChat")
                .Append(" (").Append(Format(message.CreatedAt)).Append("):\r\n");

            var attached = message.Attachments.Where(a => a.File is not null).Select(a => a.File.OriginalFileName).ToList();
            if (attached.Count > 0)
            {
                text.Append("[Attached: ").Append(string.Join(", ", attached)).Append("]\r\n");
            }
            if (message.Content.Length > 0)
            {
                // AI replies are Markdown: write them as clean plain text (no ** or #). User text is kept as typed.
                var body = message.Role == MessageRole.Assistant
                    ? PlainTextRenderer.Render(MarkdownReader.Parse(message.Content))
                    : NormalizeLineBreaks(message.Content);
                text.Append(body.TrimEnd()).Append("\r\n");
            }
            text.Append("\r\n").Append(Rule).Append("\r\n");
        }

        var fileName = $"AIChat - {SafeFileName(conversation.Title)} - {exportedAt:yyyy-MM-dd}.txt";
        // UTF-8 with a BOM so Windows editors never mistake Hindi or other non-English text for ANSI.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text.ToString())).ToArray();
        return new ExportedFile(fileName, "text/plain; charset=utf-8", bytes);
    }

    private static string Format(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

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
