using ChatGPTClone.Api.Authentication;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.DTOs.Sharing;
using ChatGPTClone.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChatGPTClone.Api.Controllers;

/// <summary>Read-only links to a snapshot of a chat: managed by its owner, opened by anyone with the link.</summary>
[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public sealed class SharingController(ISharingService sharing) : ControllerBase
{
    /// <summary>
    /// Creates a link to a snapshot of the chat as it is now (later messages stay private). The token is returned only
    /// once; sharing again replaces the link and the old one stops working.
    /// </summary>
    [HttpPost("conversations/{id:int}/share")]
    [ProducesResponseType(typeof(ShareCreatedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ShareCreatedDto>> Share(int id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store"; // the token must not be cached anywhere
        return Ok(await sharing.ShareAsync(User.GetUserId(), id, cancellationToken));
    }

    [HttpGet("conversations/{id:int}/share")]
    [ProducesResponseType(typeof(ShareStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ShareStatusDto>> Status(int id, CancellationToken cancellationToken) =>
        Ok(await sharing.GetStatusAsync(User.GetUserId(), id, cancellationToken));

    /// <summary>Stops sharing: the link returns 404 from now on.</summary>
    [HttpDelete("conversations/{id:int}/share")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke(int id, CancellationToken cancellationToken)
    {
        await sharing.RevokeAsync(User.GetUserId(), id, cancellationToken);
        return NoContent();
    }

    /// <summary>The shared snapshot (no sign-in). Wrong, revoked and deleted links all return the same 404.</summary>
    [HttpGet("share/{token}")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.SharedLinks)]
    [ProducesResponseType(typeof(SharedConversationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SharedConversationDto>> Open(string token, CancellationToken cancellationToken)
    {
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
        Response.Headers.CacheControl = "no-store"; // a revoked link must stop working at once, also via caches
        Response.Headers["Referrer-Policy"] = "no-referrer"; // the token is in the URL
        var shared = await sharing.GetSharedAsync(token, cancellationToken);
        // Returned here rather than thrown: the exception handler would clear the headers above.
        return shared is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "Link not found",
                detail: "This shared link doesn't exist or is no longer shared.")
            : Ok(shared);
    }
}
