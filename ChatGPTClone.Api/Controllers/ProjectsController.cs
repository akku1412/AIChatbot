using ChatGPTClone.Api.Authentication;
using ChatGPTClone.Api.DTOs.Projects;
using ChatGPTClone.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatGPTClone.Api.Controllers;

/// <summary>
/// The signed-in user's projects (folders of chats with shared instructions and files).
/// Other users' project ids return 404, exactly like ids that don't exist.
/// </summary>
[ApiController]
[Route("api/projects")]
[Authorize]
[Produces("application/json")]
public sealed class ProjectsController(IProjectService projects) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ProjectDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProjectDto>>> List(CancellationToken cancellationToken) =>
        Ok(await projects.ListAsync(User.GetUserId(), cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ProjectDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectDetailDto>> Get(int id, CancellationToken cancellationToken) =>
        Ok(await projects.GetAsync(User.GetUserId(), id, cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(ProjectDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProjectDto>> Create(SaveProjectRequest request, CancellationToken cancellationToken)
    {
        var created = await projects.CreateAsync(User.GetUserId(), request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.ProjectId }, created);
    }

    /// <summary>Renames the project and replaces its instructions (blank clears them).</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ProjectDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectDto>> Update(int id, SaveProjectRequest request, CancellationToken cancellationToken) =>
        Ok(await projects.UpdateAsync(User.GetUserId(), id, request, cancellationToken));

    /// <summary>Deletes the project; its chats move back to the main list and its files stay in your uploads.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await projects.DeleteAsync(User.GetUserId(), id, cancellationToken);
        return NoContent();
    }

    /// <summary>Shares one of your uploaded files with every chat in the project (up to 20 files).</summary>
    [HttpPost("{id:int}/files")]
    [ProducesResponseType(typeof(ProjectDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ProjectDetailDto>> AddFile(int id, AddProjectFileRequest request, CancellationToken cancellationToken) =>
        Ok(await projects.AddFileAsync(User.GetUserId(), id, request.FileId, cancellationToken));

    /// <summary>Removes the file from the project (the file itself is kept).</summary>
    [HttpDelete("{id:int}/files/{fileId:guid}")]
    [ProducesResponseType(typeof(ProjectDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectDetailDto>> RemoveFile(int id, Guid fileId, CancellationToken cancellationToken) =>
        Ok(await projects.RemoveFileAsync(User.GetUserId(), id, fileId, cancellationToken));
}
