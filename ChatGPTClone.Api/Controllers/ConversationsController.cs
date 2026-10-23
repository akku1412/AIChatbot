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
public sealed class ConversationsController(IConversationService conversations, IConversationExportService exports)
    : ControllerBase
{
    /// <summary>
    /// Not archived: pinned first, then most recently active. With ?archived=true: only archived chats,
    /// most recently archived first. Paginated.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ConversationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ConversationDto>>> List([FromQuery] ListConversationsQuery query,
        CancellationToken cancellationToken) =>
        Ok(await conversations.ListAsync(User.GetUserId(), query.Page, query.PageSize, query.Archived, cancellationToken));

    /// <summary>
    /// Chats (archived ones included) whose title or message text contains q (2–100 characters, case-insensitive),
    /// pinned first, with a snippet around the first match in the newest matching message.
    /// </summary>
    [HttpGet("search")]
    [ProducesResponseType(typeof(IReadOnlyList<ConversationSearchResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<ConversationSearchResultDto>>> Search([FromQuery] SearchConversationsQuery query,
        CancellationToken cancellationToken) =>
        Ok(await conversations.SearchAsync(User.GetUserId(), query.Q, query.Limit, cancellationToken));

    /// <summary>Partial update — any of { "title", "isPinned", "archived" }.</summary>
    [HttpPatch("{id:int}")]
    [ProducesResponseType(typeof(ConversationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConversationDto>> Patch(int id, PatchConversationRequest request, CancellationToken cancellationToken) =>
        Ok(await conversations.PatchAsync(User.GetUserId(), id, request, cancellationToken));

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

    /// <summary>The whole conversation (every message, oldest first) as a UTF-8 .txt download.</summary>
    [HttpGet("{id:int}/export")]
    [Produces("text/plain", "application/json")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "text/plain")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Export(int id, CancellationToken cancellationToken)
    {
        var file = await exports.ExportAsTextAsync(User.GetUserId(), id, cancellationToken);
        // File() sets "Content-Disposition: attachment" with an RFC 5987 filename*, so non-English titles survive.
        return File(file.Content, file.ContentType, file.FileName);
    }

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
