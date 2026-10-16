using ChatGPTClone.Api.Configuration;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace ChatGPTClone.Api.Middleware;

/// <summary>
/// Applies FileUploads:MaxFileSizeBytes to the upload endpoint before the body is read: the server's request
/// size limit and the multipart limit are set from configuration (attributes would need a compile-time constant),
/// and oversized requests are rejected early with 413 when Content-Length already shows they are too big.
/// </summary>
public sealed class UploadSizeLimitFilter(IOptions<FileUploadOptions> options) : IResourceFilter
{
    /// <summary>Room for the multipart boundary and headers around the file.</summary>
    private const long MultipartOverhead = 64 * 1024;

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var maxFile = options.Value.MaxFileSizeBytes;
        var maxRequest = maxFile + MultipartOverhead;
        var http = context.HttpContext;

        if (http.Request.ContentLength is { } length && length > maxRequest)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status413PayloadTooLarge,
                Title = "File too large",
                Detail = $"Files can be at most {maxFile / (1024 * 1024)} MB.",
                Extensions = { ["reason"] = "too_large" },
            })
            { StatusCode = StatusCodes.Status413PayloadTooLarge };
            return;
        }

        var sizeFeature = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
        {
            sizeFeature.MaxRequestBodySize = maxRequest;
        }

        http.Features.Set<IFormFeature>(new FormFeature(http.Request, new FormOptions
        {
            MultipartBodyLengthLimit = maxRequest,
            ValueCountLimit = 10,
        }));
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
