namespace ChatGPTClone.Api.Middleware;

/// <summary>
/// An expected, user-facing failure (bad OTP, expired session, conflict...).
/// GlobalExceptionHandler turns it into a ProblemDetails response with this status and message.
/// Title and Detail must never contain secrets, codes or tokens.
/// </summary>
public sealed class ApiException(
    int statusCode,
    string title,
    string detail,
    IReadOnlyDictionary<string, object?>? extensions = null) : Exception(detail)
{
    public int StatusCode { get; } = statusCode;
    public string Title { get; } = title;
    public IReadOnlyDictionary<string, object?> Extensions { get; } = extensions ?? new Dictionary<string, object?>();
}
