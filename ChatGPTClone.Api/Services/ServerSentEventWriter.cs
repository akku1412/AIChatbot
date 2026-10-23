using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;

namespace ChatGPTClone.Api.Services;

/// <summary>
/// Writes Server-Sent Events ("event: name" + one JSON "data:" line) and flushes each one immediately.
/// The response only switches to text/event-stream on the first event, so anything that fails before
/// that still returns a normal ProblemDetails error with the right HTTP status.
/// </summary>
public sealed class ServerSentEventWriter(HttpResponse response, JsonSerializerOptions json)
{
    public bool Started { get; private set; }

    public async Task WriteAsync(string eventName, object data, CancellationToken cancellationToken)
    {
        if (!Started)
        {
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = "text/event-stream; charset=utf-8";
            response.Headers.CacheControl = "no-cache, no-store";
            response.Headers["X-Accel-Buffering"] = "no"; // nginx and similar proxies: don't buffer the stream
            response.HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            Started = true;
        }

        // JSON never contains raw line breaks, so one "data:" line always holds the whole payload.
        var payload = JsonSerializer.Serialize(data, json);
        await response.WriteAsync($"event: {eventName}\ndata: {payload}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
