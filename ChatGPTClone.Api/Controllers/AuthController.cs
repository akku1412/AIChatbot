using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.DTOs.Auth;
using ChatGPTClone.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChatGPTClone.Api.Controllers;

[ApiController]
[Route("api/auth")]
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

    /// <summary>Step 2: check the code. Single-use, expires after 5 minutes, limited attempts.</summary>
    [HttpPost("verify-otp")]
    [ProducesResponseType(typeof(VerifyOtpResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<VerifyOtpResponse>> VerifyOtp(VerifyOtpRequest request, CancellationToken cancellationToken) =>
        Ok(await authService.VerifyOtpAsync(request.ChallengeToken, request.Code, cancellationToken));
}
