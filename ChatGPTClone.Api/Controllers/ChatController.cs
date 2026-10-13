using ChatGPTClone.Api.Authentication;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.DTOs.Chat;
using ChatGPTClone.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChatGPTClone.Api.Controllers;

[ApiController]
[Route("api/chat/{conversationId:int}/messages")]
[Authorize]
[Produces("application/json")]
public sealed class ChatController(IChatService chat) : ControllerBase
{
    /// <summary>Messages oldest-first. Pass "before" (a message id) to page backwards through history.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(MessagePageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MessagePageDto>> List(int conversationId, [FromQuery] MessagesQuery query,
        CancellationToken cancellationToken) =>
        Ok(await chat.GetMessagesAsync(User.GetUserId(), conversationId, query.Before, query.Limit, cancellationToken));

    /// <summary>Saves the user's message, gets the AI reply, saves it, and returns both.</summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.AiChat)]
    [ProducesResponseType(typeof(SendMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<SendMessageResponse>> Send(int conversationId, SendMessageRequest request,
        CancellationToken cancellationToken) =>
        Ok(await chat.SendAsync(User.GetUserId(), conversationId, request.Content, cancellationToken));
}
