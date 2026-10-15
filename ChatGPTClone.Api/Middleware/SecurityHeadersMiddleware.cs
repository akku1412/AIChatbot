namespace ChatGPTClone.Api.Middleware;

/// <summary>
/// Defensive browser headers on every response. The Content-Security-Policy allows only this site plus
/// what Google Sign-In and Google Fonts need, which blocks injected scripts even if markup slipped through.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment environment)
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self' https://accounts.google.com/gsi/client; " +
        "frame-src https://accounts.google.com; " +
        "connect-src 'self' https://accounts.google.com/gsi/; " +
        // Angular emits component styles inline, so inline styles must be allowed.
        "style-src 'self' 'unsafe-inline' https://accounts.google.com/gsi/style https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data: https://*.googleusercontent.com; " +
        "base-uri 'self'; form-action 'self'; frame-ancestors 'none'; object-src 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        // Google Sign-In's popup needs to talk back to this window.
        headers["Cross-Origin-Opener-Policy"] = "same-origin-allow-popups";

        // Swagger UI (development only) uses inline scripts, so it is exempt.
        if (!(environment.IsDevelopment() && context.Request.Path.StartsWithSegments("/swagger")))
        {
            headers.ContentSecurityPolicy = ContentSecurityPolicy;
        }

        return next(context);
    }
}
