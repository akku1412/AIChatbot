using System.ComponentModel.DataAnnotations;
using ChatGPTClone.Api.Authentication;
using ChatGPTClone.Api.DTOs.Admin;
using ChatGPTClone.Api.Models;
using ChatGPTClone.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatGPTClone.Api.Controllers;

/// <summary>
/// The admin panel: accounts, usage and feedback across all users. Admins only (the role is checked against the
/// database on every request); everyone else gets 403.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = UserRoles.Admin)]
[Produces("application/json")]
public sealed class AdminController(IAdminService admin) : ControllerBase
{
    /// <summary>Accounts, newest first; <paramref name="search"/> matches email or name.</summary>
    [HttpGet("users")]
    [ProducesResponseType(typeof(AdminUserPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminUserPageDto>> Users([FromQuery, MaxLength(256)] string? search,
        [FromQuery, Range(1, 100_000)] int page = 1, [FromQuery, Range(1, 100)] int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        Ok(await admin.GetUsersAsync(search, page, pageSize, cancellationToken));

    /// <summary>Blocks the account: it can't sign in, and its open sessions get 403 on everything.</summary>
    [HttpPost("users/{id:int}/block")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminUserDto>> Block(int id, CancellationToken cancellationToken) =>
        Ok(await admin.SetBlockedAsync(User.GetUserId(), id, blocked: true, cancellationToken));

    [HttpPost("users/{id:int}/unblock")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminUserDto>> Unblock(int id, CancellationToken cancellationToken) =>
        Ok(await admin.SetBlockedAsync(User.GetUserId(), id, blocked: false, cancellationToken));

    /// <summary>Messages, tokens, uploads and estimated cost per UTC day, plus the heaviest users.</summary>
    [HttpGet("usage")]
    [ProducesResponseType(typeof(AdminUsageDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminUsageDto>> Usage([FromQuery, Range(1, AdminService.MaxUsageDays)] int days = 30,
        CancellationToken cancellationToken = default) =>
        Ok(await admin.GetUsageAsync(days, cancellationToken));

    /// <summary>Thumbs up/down on AI replies, newest first. <paramref name="rating"/>: 1 (helpful) or -1 (not helpful).</summary>
    [HttpGet("feedback")]
    [ProducesResponseType(typeof(AdminFeedbackPageDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminFeedbackPageDto>> Feedback([FromQuery, AllowedValues(null, (short)1, (short)-1)] short? rating,
        [FromQuery, Range(1, 100_000)] int page = 1, [FromQuery, Range(1, 100)] int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        Ok(await admin.GetFeedbackAsync(rating, page, pageSize, cancellationToken));
}
