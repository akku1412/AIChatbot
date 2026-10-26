using System.Globalization;
using System.Security.Claims;
using ChatGPTClone.Api.Data;
using ChatGPTClone.Api.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.JsonWebTokens;

namespace ChatGPTClone.Api.Authentication;

/// <summary>An account's current role and block state (not the snapshot inside an access token).</summary>
public sealed record UserAccess(string Role, bool IsBlocked);

public interface IUserAccessCache
{
    /// <summary>Null when the account no longer exists.</summary>
    ValueTask<UserAccess?> GetAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Call after changing a user's role or block state, so this server sees it on the next request.</summary>
    void Invalidate(int userId);
}

/// <summary>
/// Looked up on every authenticated request, so it is cached briefly. Other servers see a change within
/// <see cref="Lifetime"/>; blocking also revokes the user's refresh tokens.
/// </summary>
public sealed class UserAccessCache(IServiceScopeFactory scopes, IMemoryCache cache) : IUserAccessCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    public async ValueTask<UserAccess?> GetAsync(int userId, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(Key(userId), out UserAccess? access))
        {
            return access;
        }

        using var scope = scopes.CreateScope();
        access = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking()
            .Where(u => u.UserId == userId)
            .Select(u => new UserAccess(u.Role, u.IsBlocked))
            .FirstOrDefaultAsync(cancellationToken);
        cache.Set(Key(userId), access, Lifetime);
        return access;
    }

    public void Invalidate(int userId) => cache.Remove(Key(userId));

    private static string Key(int userId) => $"user-access:{userId}";
}

public static class UserAccessClaims
{
    /// <summary>Role claim type in access tokens (TokenValidationParameters.RoleClaimType).</summary>
    public const string Role = "role";

    /// <summary>Added (never issued) when the account is blocked; BlockedUserMiddleware refuses the request.</summary>
    public const string Blocked = "aichat:blocked";

    /// <summary>
    /// JwtBearer OnTokenValidated: replaces the token's role with the account's current one, marks blocked accounts,
    /// and rejects tokens of deleted accounts. A demoted admin or a blocked user is affected at once, not at token expiry.
    /// </summary>
    public static async Task ApplyCurrentAccessAsync(TokenValidatedContext context)
    {
        var identity = context.Principal?.Identity as ClaimsIdentity;
        var sub = identity?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (identity is null || !int.TryParse(sub, NumberStyles.None, CultureInfo.InvariantCulture, out var userId))
        {
            context.Fail("The token has no valid subject.");
            return;
        }

        var access = await context.HttpContext.RequestServices.GetRequiredService<IUserAccessCache>()
            .GetAsync(userId, context.HttpContext.RequestAborted);
        if (access is null)
        {
            context.Fail("The account no longer exists.");
            return;
        }

        foreach (var claim in identity.FindAll(Role).ToList())
        {
            identity.RemoveClaim(claim);
        }
        identity.AddClaim(new Claim(Role, access.Role));
        if (access.IsBlocked)
        {
            identity.AddClaim(new Claim(Blocked, "true"));
        }
    }
}
