using ChatGPTClone.Api.Authentication;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.DTOs.Chat;
using ChatGPTClone.Api.Middleware;
using ChatGPTClone.Api.Services;
using ChatGPTClone.Api.Services.Export;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChatGPTClone.Api.Controllers;

/// <summary>Single chat messages. Ownership is checked through the message's conversation (404 for anyone else).</summary>
[ApiController]
[Route("api/messages")]
[Authorize]
[Produces("application/json")]
public sealed class MessagesController(
    IResponseExportService exports,
    IChatService chat,
    IGenerationRegistry generations,
    ChatStreamResponder streamer) : ControllerBase
{
    /// <summary>
    /// Streams a new version of this AI reply (or a first reply, when the id is a question without one), using the
    /// thread up to its question. Earlier versions are kept ("‹ 2/3 ›"). Same events as POST /api/chat/{id}/messages/stream.
    /// </summary>
    [HttpPost("{messageId:long}/regenerate")]
    [EnableRateLimiting(RateLimitPolicies.AiChat)]
    [Produces("text/event-stream", "application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task Regenerate(long messageId, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var conversationId = await chat.GetConversationIdAsync(userId, messageId, cancellationToken);
        using var generation = generations.Start(userId, conversationId);
        var turn = await chat.PrepareRegenerationAsync(userId, messageId, cancellationToken);
        await streamer.StreamAsync(Response, turn, generation, cancellationToken);
    }

    /// <summary>
    /// Edit and resend: saves a new version of this user message (same position in the thread, same attachments) and
    /// streams a new reply to it. The original message and everything after it stay available as the earlier version.
    /// </summary>
    [HttpPut("{messageId:long}")]
    [EnableRateLimiting(RateLimitPolicies.AiChat)]
    [Produces("text/event-stream", "application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task Edit(long messageId, EditMessageRequest request, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var conversationId = await chat.GetConversationIdAsync(userId, messageId, cancellationToken);
        using var generation = generations.Start(userId, conversationId);
        var turn = await chat.PrepareEditAsync(userId, messageId, request.Content, cancellationToken);
        await streamer.StreamAsync(Response, turn, generation, cancellationToken);
    }

    /// <summary>
    /// Shows another version of this message (the "‹ ›" arrows) and returns the newest page of the resulting thread.
    /// 409 while a reply is being generated in the conversation.
    /// </summary>
    [HttpPost("{messageId:long}/switch-version")]
    [ProducesResponseType(typeof(MessagePageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MessagePageDto>> SwitchVersion(long messageId, SwitchVersionRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var conversationId = await chat.GetConversationIdAsync(userId, messageId, cancellationToken);
        using var generation = generations.Start(userId, conversationId); // not while a reply is streaming
        return Ok(await chat.SwitchVersionAsync(userId, messageId, request.VersionNumber, cancellationToken));
    }

    /// <summary>
    /// The message generated as a downloadable file: format = txt (plain text), pdf (formatted document),
    /// docx (editable Word document) or xlsx (its tables as worksheets; 422 when it has none).
    /// </summary>
    [HttpGet("{messageId:long}/export")]
    [EnableRateLimiting(RateLimitPolicies.Exports)]
    [Produces("application/octet-stream", "application/json")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Export(long messageId, [FromQuery] string? format, CancellationToken cancellationToken)
    {
        if (!ExportFormats.TryParse(format, out var parsed))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "Unsupported format",
                "Choose one of: txt, pdf, docx, xlsx.", new Dictionary<string, object?> { ["reason"] = "unsupported_format" });
        }

        var file = await exports.ExportMessageAsync(User.GetUserId(), messageId, parsed, cancellationToken);
        Response.Headers.CacheControl = "private, no-store";
        return File(file.Content, file.ContentType, file.FileName);
    }
}
