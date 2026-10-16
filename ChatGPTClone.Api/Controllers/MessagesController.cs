using ChatGPTClone.Api.Authentication;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Middleware;
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
public sealed class MessagesController(IResponseExportService exports) : ControllerBase
{
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
