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
}
