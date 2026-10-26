using System.Text;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Middleware;
using ChatGPTClone.Api.Models;

namespace ChatGPTClone.Api.Services.Export;

public interface IResponseExportService
{
    /// <summary>One chat message (normally an AI reply) generated as a real TXT/PDF/DOCX/XLSX file. 404 unless it's the user's.</summary>
    Task<ExportedFile> ExportMessageAsync(int userId, long messageId, ExportFormat format, CancellationToken cancellationToken);
}

public sealed class ResponseExportService(IMessageRepository messages, ILogger<ResponseExportService> logger) : IResponseExportService
{
    public async Task<ExportedFile> ExportMessageAsync(int userId, long messageId, ExportFormat format, CancellationToken cancellationToken)
    {
        var message = await messages.GetOwnedAsync(messageId, userId, cancellationToken);
        if (message is null || message.Role == MessageRole.System)
        {
            throw new ApiException(StatusCodes.Status404NotFound, "Message not found",
                "This message doesn't exist or has been deleted.", Reason("message_not_found"));
        }

        var title = message.Conversation.Title;
        var context = new ExportContext(title, message.CreatedAt);
        // Citation markers [file:123] become "(report.pdf, Page 3)".
        var blocks = MarkdownReader.Parse(Files.Citations.Readable(message));

        byte[] content;
        try
        {
            content = format switch
            {
                // UTF-8 with BOM so Windows editors never mistake non-English text for ANSI.
                ExportFormat.Txt => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(PlainTextRenderer.Render(blocks))],
                ExportFormat.Pdf => PdfRenderer.Render(blocks, context),
                ExportFormat.Docx => DocxRenderer.Render(blocks, context),
                _ => XlsxRenderer.Render(blocks, context),
            };
        }
        catch (ExportNotPossibleException ex)
        {
            throw new ApiException(StatusCodes.Status422UnprocessableEntity, "Can't export in this format", ex.Message, Reason("no_tabular_data"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ApiException)
        {
            logger.LogError(ex, "Generating a {Format} export of message {MessageId} failed", format, messageId);
            throw new ApiException(StatusCodes.Status500InternalServerError, "Export failed",
                "We couldn't create this file. Please try again or choose another format.", Reason("export_failed"));
        }

        var fileName = $"AIChat reply - {ConversationExportService.SafeFileName(title)} - {message.CreatedAt:yyyy-MM-dd HHmm}{format.Extension()}";
        return new ExportedFile(fileName, format.ContentType(), content);
    }

    private static Dictionary<string, object?> Reason(string reason) => new() { ["reason"] = reason };
}
