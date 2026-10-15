using System.Net;

namespace ChatGPTClone.Api.Services;

internal static class OtpEmailTemplate
{
    public const string Subject = "Your AIChat verification code";

    public static string Html(string? fullName, string code, int expiryMinutes)
    {
        var name = string.IsNullOrWhiteSpace(fullName) ? "there" : WebUtility.HtmlEncode(fullName);
        return $"""
            <!doctype html>
            <html><body style="margin:0;padding:24px;background:#f7f7f8;font-family:Segoe UI,Arial,sans-serif;color:#1f1f24">
              <table role="presentation" width="100%" style="max-width:480px;margin:0 auto;background:#ffffff;border:1px solid #e3e3e8;border-radius:12px">
                <tr><td style="padding:32px">
                  <p style="margin:0 0 16px;font-size:16px">Hi {name},</p>
                  <p style="margin:0 0 24px;font-size:16px">Use this code to finish signing in to AIChat:</p>
                  <p style="margin:0 0 24px;font-size:32px;font-weight:700;letter-spacing:8px;text-align:center;font-family:Consolas,monospace">{code}</p>
                  <p style="margin:0 0 8px;font-size:14px;color:#6b6b76">The code expires in {expiryMinutes} minutes and can be used once.</p>
                  <p style="margin:0;font-size:14px;color:#6b6b76">If you did not try to sign in, you can ignore this email. Never share this code with anyone.</p>
                </td></tr>
              </table>
            </body></html>
            """;
    }

    public static string Text(string? fullName, string code, int expiryMinutes) =>
        $"""
        Hi {(string.IsNullOrWhiteSpace(fullName) ? "there" : fullName)},

        Your AIChat verification code is: {code}

        It expires in {expiryMinutes} minutes and can be used once.
        If you did not try to sign in, ignore this email. Never share this code with anyone.
        """;
}
