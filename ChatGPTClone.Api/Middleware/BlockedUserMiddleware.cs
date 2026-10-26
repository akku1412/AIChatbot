using ChatGPTClone.Api.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace ChatGPTClone.Api.Middleware;

/// <summary>
/// Blocked accounts get 403 "account_blocked" on every endpoint that needs sign-in. Anonymous endpoints (sign-in,
/// logout, shared links) still work, so the app can sign the user out cleanly.
/// </summary>
public sealed class BlockedUserMiddleware(RequestDelegate next)
{
    public const string Message = "Your account has been blocked. Contact the administrator if you think this is a mistake.";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.HasClaim(UserAccessClaims.Blocked, "true")
            && context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is null)
        {
            await Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Account blocked",
                    detail: Message,
                    extensions: new Dictionary<string, object?> { ["reason"] = "account_blocked" })
                .ExecuteAsync(context);
            return;
        }

        await next(context);
    }
}
