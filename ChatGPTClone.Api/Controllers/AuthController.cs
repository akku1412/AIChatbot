using ChatGPTClone.Api.Authentication;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.DTOs.Auth;
using ChatGPTClone.Api.DTOs.Users;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChatGPTClone.Api.Controllers;

/// <summary>
/// Sign-in with an emailed one-time code or with Google. Both end with the same session:
/// a short-lived access token in the body and a rotating refresh token in an HttpOnly cookie.
/// </summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Auth)]
[Produces("application/json")]
public sealed class AuthController(IAuthService authService, IUserService userService) : ControllerBase
{
    /// <summary>Email sign-in, step 1: emails a 6-digit code (valid 5 minutes). Works for new and existing users alike.</summary>
    [HttpPost("send-otp")]
    [ProducesResponseType(typeof(OtpSentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OtpSentResponse>> SendOtp(EmailOtpRequest request, CancellationToken cancellationToken) =>
        Ok(await authService.SendOtpAsync(request.Email, cancellationToken));

    /// <summary>Sends a new code after the 60-second cooldown; the previous code stops working.</summary>
    [HttpPost("resend-otp")]
    [ProducesResponseType(typeof(OtpSentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OtpSentResponse>> ResendOtp(EmailOtpRequest request, CancellationToken cancellationToken) =>
        Ok(await authService.ResendOtpAsync(request.Email, cancellationToken));

    /// <summary>
    /// Email sign-in, step 2: checks the code (single use, 5 attempts). Registers the account if the email
    /// is new, then returns the access token and sets the refresh cookie.
    /// </summary>
    [HttpPost("verify-otp")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthResponse>> VerifyOtp(VerifyOtpRequest request, CancellationToken cancellationToken) =>
        Ok(WithCookie(await authService.VerifyOtpAsync(request.Email, request.Code, cancellationToken)));

    /// <summary>Google sign-in: validates the Google ID token on the server and signs in (no OTP).</summary>
    [HttpPost("google")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Google(GoogleLoginRequest request, CancellationToken cancellationToken) =>
        Ok(WithCookie(await authService.SignInWithGoogleAsync(request.IdToken, cancellationToken)));

    /// <summary>Exchanges the refresh cookie for a new access token (and a rotated cookie).</summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken cancellationToken)
    {
        var refreshToken = RefreshTokenCookie.Read(Request)
            ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Not signed in", "Please sign in.");

        try
        {
            return Ok(WithCookie(await authService.RefreshAsync(refreshToken, cancellationToken)));
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

    /// <summary>The signed-in user (same data as GET /api/users/me).</summary>
    [HttpGet("me")]
    [Authorize]
    [DisableRateLimiting]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserDto>> Me(CancellationToken cancellationToken) =>
        Ok(await userService.GetAsync(User.GetUserId(), cancellationToken));

    private AuthResponse WithCookie(AuthSession session)
    {
        RefreshTokenCookie.Write(Response, session.RefreshToken);
        return session.Response;
    }
}
