using ChatGPTClone.Api.Authentication;
using ChatGPTClone.Api.DTOs.Users;
using ChatGPTClone.Api.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatGPTClone.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
[Produces("application/json")]
public sealed class UsersController(IUserService userService) : ControllerBase
{
    /// <summary>The signed-in user's profile (identified by the JWT, never by a request parameter).</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserDto>> GetMe(CancellationToken cancellationToken) =>
        Ok(await userService.GetAsync(User.GetUserId(), cancellationToken));

    /// <summary>Updates the display name. Email and photo are managed by Google.</summary>
    [HttpPut("me")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserDto>> UpdateMe(UpdateProfileRequest request, CancellationToken cancellationToken) =>
        Ok(await userService.UpdateProfileAsync(User.GetUserId(), request.FullName, cancellationToken));

    /// <summary>Custom instructions ("About you", "How to respond"); null fields are not set.</summary>
    [HttpGet("me/settings")]
    [ProducesResponseType(typeof(UserSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserSettingsDto>> GetSettings(CancellationToken cancellationToken) =>
        Ok(await userService.GetSettingsAsync(User.GetUserId(), cancellationToken));

    /// <summary>
    /// Replaces the custom instructions (each at most 1,500 characters; blank clears). They are added to every chat's
    /// system prompt as preferences, below the app's own rules, so they can't override them.
    /// </summary>
    [HttpPut("me/settings")]
    [ProducesResponseType(typeof(UserSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserSettingsDto>> UpdateSettings(UpdateUserSettingsRequest request, CancellationToken cancellationToken) =>
        Ok(await userService.UpdateSettingsAsync(User.GetUserId(), request, cancellationToken));
}
