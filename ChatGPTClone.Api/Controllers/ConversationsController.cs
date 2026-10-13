using ChatGPTClone.Api.Authentication;
using ChatGPTClone.Api.DTOs.Chat;
using ChatGPTClone.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatGPTClone.Api.Controllers;

/// <summary>The signed-in user's conversations. The user id always comes from the JWT.</summary>
[ApiController]
[Route("api/conversations")]
[Authorize]
[Produces("application/json")]
public sealed class ConversationsController(IConversationService conversations) : ControllerBase
{
    /// <summary>Most recently active first, paginated.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ConversationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ConversationDto>>> List([FromQuery] ListConversationsQuery query,
        CancellationToken cancellationToken) =>
        Ok(await conversations.ListAsync(User.GetUserId(), query.Page, query.PageSize, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ConversationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConversationDto>> Get(int id, CancellationToken cancellationToken) =>
        Ok(await conversations.GetAsync(User.GetUserId(), id, cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(ConversationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ConversationDto>> Create(CreateConversationRequest request, CancellationToken cancellationToken)
    {
        var created = await conversations.CreateAsync(User.GetUserId(), request.Title, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.ConversationId }, created);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ConversationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConversationDto>> Rename(int id, RenameConversationRequest request,
        CancellationToken cancellationToken) =>
        Ok(await conversations.RenameAsync(User.GetUserId(), id, request.Title, cancellationToken));

    /// <summary>Deletes the conversation and all of its messages.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await conversations.DeleteAsync(User.GetUserId(), id, cancellationToken);
        return NoContent();
    }
}
