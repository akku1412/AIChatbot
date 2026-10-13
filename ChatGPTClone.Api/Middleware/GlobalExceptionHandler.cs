using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ChatGPTClone.Api.Middleware;

/// <summary>
/// Turns any unhandled exception into an RFC 7807 ProblemDetails response.
/// ApiException carries a safe, user-facing status and message. Anything else becomes a generic 500:
/// clients never see stack traces or exception messages; the server log gets the exception plus a
/// trace id so a user-reported error can be matched to the log entry.
/// </summary>
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // The client disconnected or cancelled; nothing useful to send back.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("Request {Method} {Path} was cancelled by the client",
                httpContext.Request.Method, httpContext.Request.Path);
            httpContext.Response.StatusCode = 499;
            return true;
        }

        ProblemDetails problem;
        if (exception is ApiException apiException)
        {
            logger.LogInformation("Request {Method} {Path} failed with {StatusCode}: {Title}",
                httpContext.Request.Method, httpContext.Request.Path, apiException.StatusCode, apiException.Title);

            problem = new ProblemDetails
            {
                Status = apiException.StatusCode,
                Title = apiException.Title,
                Detail = apiException.Message,
            };
            foreach (var (key, value) in apiException.Extensions)
            {
                problem.Extensions[key] = value;
            }

            if (apiException.Extensions.TryGetValue("retryAfterSeconds", out var retryAfter) && retryAfter is not null)
            {
                httpContext.Response.Headers.RetryAfter = retryAfter.ToString();
            }
        }
        else
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);

            problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Something went wrong",
                Detail = "An unexpected error occurred. Please try again later.",
            };
        }

        httpContext.Response.StatusCode = problem.Status!.Value;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }
}
