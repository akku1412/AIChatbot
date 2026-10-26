using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Middleware;
using ChatGPTClone.Api.Services;
using ChatGPTClone.Api.Services.Audio;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChatGPTClone.Api.Controllers;

/// <summary>POST /api/audio/transcribe result: the words spoken, for the composer.</summary>
public sealed record TranscriptionDto(string Text);

/// <summary>Voice input: speech to text. Recordings are sent to the provider and never stored.</summary>
[ApiController]
[Route("api/audio")]
[Authorize]
[Produces("application/json")]
public sealed class AudioController(ISpeechToText speech, ILogger<AudioController> logger) : ControllerBase
{
    /// <summary>Room for the multipart boundary and headers around the recording.</summary>
    private const long MultipartOverhead = 64 * 1024;

    /// <summary>
    /// Transcribes one recording (multipart field "audio": WebM, Ogg, MP4/M4A, WAV or MP3, at most 10 MB). The format is
    /// checked from the bytes; a recording with no recognisable speech returns 422.
    /// </summary>
    [HttpPost("transcribe")]
    [EnableRateLimiting(RateLimitPolicies.AiChat)] // speech-to-text costs money like a chat message
    [RequestSizeLimit(TranscriptionOptions.MaxAudioBytes + MultipartOverhead)]
    [RequestFormLimits(MultipartBodyLengthLimit = TranscriptionOptions.MaxAudioBytes + MultipartOverhead)]
    [ProducesResponseType(typeof(TranscriptionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<TranscriptionDto>> Transcribe(IFormFile? audio, CancellationToken cancellationToken)
    {
        if (!speech.IsConfigured)
        {
            throw new ApiException(StatusCodes.Status503ServiceUnavailable, "Voice input unavailable",
                "Speech-to-text isn't set up on this server.", new Dictionary<string, object?> { ["reason"] = "transcription_not_configured" });
        }
        if (audio is null || audio.Length == 0)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "No recording", "Send the recording in the \"audio\" field.");
        }
        if (audio.Length > TranscriptionOptions.MaxAudioBytes)
        {
            throw new ApiException(StatusCodes.Status413PayloadTooLarge, "Recording too long",
                $"Recordings can be at most {TranscriptionOptions.MaxAudioBytes / (1024 * 1024)} MB. Record a shorter message.");
        }

        await using var buffer = new MemoryStream((int)audio.Length);
        await audio.CopyToAsync(buffer, cancellationToken);
        var fileName = AudioFormats.FileNameFor(buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, 16)))
            ?? throw new ApiException(StatusCodes.Status415UnsupportedMediaType, "Unsupported recording",
                "Only WebM, Ogg, MP4/M4A, WAV or MP3 audio can be transcribed.");
        buffer.Position = 0;

        string text;
        try
        {
            text = await speech.TranscribeAsync(buffer, fileName, cancellationToken);
        }
        catch (AiProviderException ex)
        {
            throw ChatService.ToApiException(ex.Kind);
        }
        logger.LogInformation("Transcribed {Bytes} bytes of {Format} into {Characters} characters", audio.Length, fileName, text.Length);

        return string.IsNullOrWhiteSpace(text)
            ? throw new ApiException(StatusCodes.Status422UnprocessableEntity, "No speech recognized",
                "We couldn't hear any words in that recording. Try again a little closer to the microphone.")
            : Ok(new TranscriptionDto(text));
    }
}
