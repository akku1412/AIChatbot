using System.Text;
using ChatGPTClone.Api.Authentication;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.DTOs.Chat;
using ChatGPTClone.Api.Middleware;
using ChatGPTClone.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace ChatGPTClone.Api.Controllers;

[ApiController]
[Route("api/chat/{conversationId:int}/messages")]
[Authorize]
[Produces("application/json")]
public sealed class ChatController(
    IChatService chat,
    IGenerationRegistry generations,
    IOptions<JsonOptions> jsonOptions,
    ILogger<ChatController> logger) : ControllerBase
{
    /// <summary>Messages oldest-first. Pass "before" (a message id) to page backwards through history.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(MessagePageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MessagePageDto>> List(int conversationId, [FromQuery] MessagesQuery query,
        CancellationToken cancellationToken) =>
        Ok(await chat.GetMessagesAsync(User.GetUserId(), conversationId, query.Before, query.Limit, cancellationToken));

    /// <summary>
    /// Saves the user's message (with optional attachmentIds from POST /api/files/upload), gets the AI reply
    /// using the attached documents as context, saves it, and returns both. See /stream for the streaming version.
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
        CancellationToken cancellationToken) =>
        Ok(await chat.SendAsync(User.GetUserId(), conversationId, request.Content, request.AttachmentIds, cancellationToken));

    /// <summary>
    /// Same as POST, but the reply streams as Server-Sent Events: "delta" {text} pieces as they are generated,
    /// then "done" (StreamTurnResult, with the saved messages), or "error" (ProblemDetails fields) if the AI fails
    /// part-way. Errors before the first piece are normal HTTP errors. POST …/stop stops it; the partial reply is kept.
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
        var turn = await chat.PrepareTurnAsync(userId, conversationId, request.Content, request.AttachmentIds, cancellationToken);
        await StreamTurnAsync(turn, generation, cancellationToken);
    }

    /// <summary>
    /// Streams a new answer to the conversation's last question (same events as /stream). The previous reply is
    /// replaced once the new one is saved; 409 when the conversation has nothing to regenerate.
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
        var turn = await chat.PrepareRegenerationAsync(userId, conversationId, cancellationToken);
        await StreamTurnAsync(turn, generation, cancellationToken);
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

    private async Task StreamTurnAsync(ChatTurn turn, GenerationHandle generation, CancellationToken requestAborted)
    {
        using var stopOrAbort = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, generation.StopToken);
        var events = new ServerSentEventWriter(Response, jsonOptions.Value.JsonSerializerOptions);
        var reply = new StringBuilder();
        int? totalTokens = null;
        var truncated = false;

        try
        {
            await foreach (var update in chat.StreamReplyAsync(turn, stopOrAbort.Token))
            {
                totalTokens = update.TotalTokens ?? totalTokens;
                truncated |= update.Truncated;
                if (update.Text.Length > 0)
                {
                    reply.Append(update.Text);
                    await events.WriteAsync("delta", new { text = update.Text }, requestAborted);
                }
            }
        }
        catch (OperationCanceledException) when (stopOrAbort.IsCancellationRequested)
        {
            // Stop button, or the browser closed the stream: keep the question and whatever was generated so far.
            var stopped = await chat.SaveTurnAsync(turn, reply.ToString(), null, stopped: true, truncated, CancellationToken.None);
            if (!requestAborted.IsCancellationRequested)
            {
                await events.WriteAsync("done", stopped, CancellationToken.None);
            }
            return;
        }
        catch (ApiException ex) when (events.Started)
        {
            // The AI failed part-way: nothing is saved (like a failed non-streaming send); the client restores the question.
            await events.WriteAsync("error", Problem(ex), CancellationToken.None);
            return;
        }
        catch (Exception ex) when (events.Started && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Streaming a reply in conversation {ConversationId} failed", turn.Conversation.ConversationId);
            await events.WriteAsync("error",
                new { status = 500, title = "Something went wrong", detail = "The reply couldn't be completed. Please try again." },
                CancellationToken.None);
            return;
        }

        // Saved even if the browser disconnects right now, so the reply is in the history next time.
        var result = await chat.SaveTurnAsync(turn, reply.ToString(), totalTokens, stopped: false, truncated, CancellationToken.None);
        if (!requestAborted.IsCancellationRequested)
        {
            await events.WriteAsync("done", result, CancellationToken.None);
        }
    }

    private static Dictionary<string, object?> Problem(ApiException ex)
    {
        var problem = new Dictionary<string, object?> { ["status"] = ex.StatusCode, ["title"] = ex.Title, ["detail"] = ex.Message };
        foreach (var (key, value) in ex.Extensions)
        {
            problem[key] = value;
        }
        return problem;
    }
}
