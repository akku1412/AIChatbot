using ChatGPTClone.Api.Authentication;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.DTOs.Files;
using ChatGPTClone.Api.Middleware;
using ChatGPTClone.Api.Services.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChatGPTClone.Api.Controllers;

/// <summary>
/// The signed-in user's documents (PDF, Word, Excel). Upload returns a fileId to attach to a chat message
/// (POST /api/chat/{conversationId}/messages with "attachmentIds"). Other users' files always return 404.
/// </summary>
[ApiController]
[Route("api/files")]
[Authorize]
[Produces("application/json")]
public sealed class FilesController(IFileService files) : ControllerBase
{
    /// <summary>
    /// Uploads one file (multipart/form-data, field "file"). The file is validated (size, type, real format),
    /// stored privately and its text extracted; the response has status "completed" when it can be used.
    /// </summary>
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting(RateLimitPolicies.Uploads)]
    [ServiceFilter(typeof(UploadSizeLimitFilter))]
    [ProducesResponseType(typeof(UploadedFileDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<UploadedFileDto>> Upload(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "No file",
                "Choose a file to upload (form field \"file\").",
                new Dictionary<string, object?> { ["reason"] = "missing" });
        }

        var uploaded = await files.UploadAsync(User.GetUserId(), file, cancellationToken);
        return CreatedAtAction(nameof(Get), new { fileId = uploaded.FileId }, uploaded);
    }

    /// <summary>The limits the client should enforce before uploading.</summary>
    [HttpGet("limits")]
    [ProducesResponseType(typeof(UploadLimitsDto), StatusCodes.Status200OK)]
    public ActionResult<UploadLimitsDto> Limits() => Ok(files.Limits);

    /// <summary>The user's most recent uploads.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<UploadedFileDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UploadedFileDto>>> List([FromQuery] int take = 50, CancellationToken cancellationToken = default) =>
        Ok(await files.ListAsync(User.GetUserId(), Math.Clamp(take, 1, 200), cancellationToken));

    /// <summary>Metadata and processing status of one file.</summary>
    [HttpGet("{fileId:guid}")]
    [ProducesResponseType(typeof(UploadedFileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UploadedFileDto>> Get(Guid fileId, CancellationToken cancellationToken) =>
        Ok(await files.GetAsync(User.GetUserId(), fileId, cancellationToken));

    /// <summary>Deletes the file, its extracted text and its links to chat messages.</summary>
    [HttpDelete("{fileId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid fileId, CancellationToken cancellationToken)
    {
        await files.DeleteAsync(User.GetUserId(), fileId, cancellationToken);
        return NoContent();
    }
}
