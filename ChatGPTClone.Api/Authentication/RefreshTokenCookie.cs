using ChatGPTClone.Api.Interfaces;

namespace ChatGPTClone.Api.Authentication;

/// <summary>
/// The refresh token cookie: HttpOnly (JavaScript can never read it), Secure, SameSite=Strict, and scoped
/// to /api/auth so it is only sent to refresh/logout, never with ordinary API calls.
/// </summary>
public static class RefreshTokenCookie
{
    public const string Name = "aichat_refresh";
    private const string Path = "/api/auth";

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    public static void Write(HttpResponse response, IssuedRefreshToken token) =>
        response.Cookies.Append(Name, token.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = Path,
            Expires = token.ExpiresAt,
            IsEssential = true,
        });

    public static void Clear(HttpResponse response) =>
        response.Cookies.Delete(Name, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = Path,
        });
}
