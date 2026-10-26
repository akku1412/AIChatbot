using ChatGPTClone.Api.Authentication;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.DTOs.Chat;
using ChatGPTClone.Api.Services;
using ChatGPTClone.Api.Services.Export;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
    /// most recently archived first. Without ?projectId: chats in no project; with it: that project's chats. Paginated.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ConversationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ConversationDto>>> List([FromQuery] ListConversationsQuery query,
        CancellationToken cancellationToken) =>
        Ok(await conversations.ListAsync(User.GetUserId(), query.Page, query.PageSize, query.Archived, query.ProjectId, cancellationToken));

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

    /// <summary>Partial update — any of { "title", "isPinned", "archived", "modelId" } (modelId from GET /api/models).</summary>
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
        var created = await conversations.CreateAsync(User.GetUserId(), request.Title, request.ModelId, request.ProjectId, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.ConversationId }, created);
    }

    /// <summary>Moves the chat into one of your projects ({ "projectId": 3 }) or back to the main list ({ "projectId": null }).</summary>
    [HttpPut("{id:int}/project")]
    [ProducesResponseType(typeof(ConversationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConversationDto>> Move(int id, MoveConversationRequest request, CancellationToken cancellationToken) =>
        Ok(await conversations.MoveAsync(User.GetUserId(), id, request.ProjectId, cancellationToken));

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ConversationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConversationDto>> Rename(int id, RenameConversationRequest request,
        CancellationToken cancellationToken) =>
        Ok(await conversations.RenameAsync(User.GetUserId(), id, request.Title, cancellationToken));

    /// <summary>
    /// The whole conversation (the visible thread, oldest first, "You"/"AIChat" headings) as a download:
    /// ?format=txt (default), pdf or docx.
    /// </summary>
    [HttpGet("{id:int}/export")]
    [EnableRateLimiting(RateLimitPolicies.Exports)]
    [Produces("text/plain", "application/pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/json")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Export(int id, [FromQuery] string? format, CancellationToken cancellationToken)
    {
        if (!ExportFormats.TryParse(format ?? "txt", out var parsed))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unsupported format",
                detail: "Whole chats can be exported as txt, pdf or docx.");
        }
        var file = await exports.ExportAsync(User.GetUserId(), id, parsed, cancellationToken);
        Response.Headers.CacheControl = "private, no-store";
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
