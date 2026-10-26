using ChatGPTClone.Api.Authentication;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.DTOs.Chat;
using ChatGPTClone.Api.Middleware;
using ChatGPTClone.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChatGPTClone.Api.Controllers;

[ApiController]
[Route("api/chat/{conversationId:int}/messages")]
[Authorize]
[Produces("application/json")]
public sealed class ChatController(IChatService chat, IGenerationRegistry generations, ChatStreamResponder streamer) : ControllerBase
{
    /// <summary>
    /// The visible thread, oldest first (the active version of every message; see the version fields).
    /// Pass "before" (a message id) to page backwards through history.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(MessagePageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MessagePageDto>> List(int conversationId, [FromQuery] MessagesQuery query,
        CancellationToken cancellationToken) =>
        Ok(await chat.GetMessagesAsync(User.GetUserId(), conversationId, query.Before, query.Limit, cancellationToken));

    /// <summary>
    /// Saves the user's message (with optional attachmentIds from POST /api/files/upload), gets the AI reply
    /// using the attached documents as context, saves it, and returns both. Non-streaming fallback of /stream.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.AiChat)]
    [ProducesResponseType(typeof(SendMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<SendMessageResponse>> Send(int conversationId, SendMessageRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        using var generation = generations.Start(userId, conversationId);
        return Ok(await chat.SendAsync(userId, conversationId, request.Content, request.AttachmentIds, cancellationToken, request.WebSearch));
    }

    /// <summary>
    /// Same as POST, but the reply streams as Server-Sent Events: "delta" {text} pieces as they are generated,
    /// then "done" (StreamTurnResult, with the saved messages), or "error" (ProblemDetails fields) if the AI fails
    /// part-way. Errors before the first piece are normal HTTP errors. Stop with POST …/stop or by aborting the
    /// request; the partial reply is saved with isTruncated = true.
    /// </summary>
    [HttpPost("stream")]
    [EnableRateLimiting(RateLimitPolicies.AiChat)]
    [Produces("text/event-stream", "application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task Stream(int conversationId, SendMessageRequest request, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        using var generation = generations.Start(userId, conversationId);
        var turn = await chat.PrepareTurnAsync(userId, conversationId, request.Content, request.AttachmentIds, cancellationToken, request.WebSearch);
        await streamer.StreamAsync(Response, turn, generation, cancellationToken);
    }

    /// <summary>
    /// Streams a new version of the conversation's latest reply (or a reply to a trailing question). Kept for
    /// compatibility; POST /api/messages/{messageId}/regenerate targets a specific reply.
    /// </summary>
    [HttpPost("regenerate")]
    [EnableRateLimiting(RateLimitPolicies.AiChat)]
    [Produces("text/event-stream", "application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task Regenerate(int conversationId, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        using var generation = generations.Start(userId, conversationId);
        var turn = await chat.PrepareLatestRegenerationAsync(userId, conversationId, cancellationToken);
        await streamer.StreamAsync(Response, turn, generation, cancellationToken);
    }

    /// <summary>Stops the reply being generated in this conversation (204), or 404 when nothing is running.</summary>
    [HttpPost("stop")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public IActionResult Stop(int conversationId) =>
        generations.Stop(User.GetUserId(), conversationId)
            ? NoContent()
            : throw new ApiException(StatusCodes.Status404NotFound, "Nothing to stop",
                "No reply is being generated in this conversation.", new Dictionary<string, object?> { ["reason"] = "not_generating" });
}
