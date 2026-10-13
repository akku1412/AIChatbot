using ChatGPTClone.Api.Authentication;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.DTOs.Auth;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChatGPTClone.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Auth)]
[Produces("application/json")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    /// <summary>Step 1: verify the Google ID token, create/update the user, and email a 6-digit code.</summary>
    [HttpPost("google")]
    [ProducesResponseType(typeof(OtpChallengeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OtpChallengeResponse>> Google(GoogleLoginRequest request, CancellationToken cancellationToken) =>
        Ok(await authService.SignInWithGoogleAsync(request.IdToken, cancellationToken));

    /// <summary>Resend: invalidates the previous code and emails a new one (cooldown applies).</summary>
    [HttpPost("send-otp")]
    [ProducesResponseType(typeof(OtpChallengeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OtpChallengeResponse>> SendOtp(SendOtpRequest request, CancellationToken cancellationToken) =>
        Ok(await authService.ResendOtpAsync(request.ChallengeToken, cancellationToken));

    /// <summary>
    /// Step 2: check the code. On success returns the access token and sets the HttpOnly refresh cookie.
    /// This is the only endpoint that starts a session.
    /// </summary>
    [HttpPost("verify-otp")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> VerifyOtp(VerifyOtpRequest request, CancellationToken cancellationToken)
    {
        var session = await authService.VerifyOtpAsync(request.ChallengeToken, request.Code, cancellationToken);
        RefreshTokenCookie.Write(Response, session.RefreshToken);
        return Ok(session.Response);
    }

    /// <summary>Exchanges the refresh cookie for a new access token (and a rotated cookie).</summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken cancellationToken)
    {
        var refreshToken = RefreshTokenCookie.Read(Request);
        if (refreshToken is null)
        {
            throw new ApiException(StatusCodes.Status401Unauthorized, "Not signed in", "Please sign in.");
        }

        try
        {
            var session = await authService.RefreshAsync(refreshToken, cancellationToken);
            RefreshTokenCookie.Write(Response, session.RefreshToken);
            return Ok(session.Response);
        }
        catch (ApiException)
        {
            RefreshTokenCookie.Clear(Response);
            throw;
        }
    }

    /// <summary>Revokes the refresh token and clears the cookie. Works even if the access token has expired.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await authService.LogoutAsync(RefreshTokenCookie.Read(Request), cancellationToken);
        RefreshTokenCookie.Clear(Response);
        return NoContent();
    }
}
