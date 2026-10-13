using System.Globalization;
using System.Security.Claims;
using ChatGPTClone.Api.Middleware;
using Microsoft.IdentityModel.JsonWebTokens;

namespace ChatGPTClone.Api.Authentication;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The authenticated user's id from the validated JWT "sub" claim. Controllers use ONLY this for
    /// ownership checks, never an id supplied in the request.
    /// </summary>
    public static int GetUserId(this ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return int.TryParse(sub, NumberStyles.None, CultureInfo.InvariantCulture, out var userId)
            ? userId
            : throw new ApiException(StatusCodes.Status401Unauthorized, "Not signed in", "Please sign in again.");
    }
}
