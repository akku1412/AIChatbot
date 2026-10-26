using System.ClientModel;
using System.ClientModel.Primitives;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Services.Ai;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Audio;

namespace ChatGPTClone.Api.Services.Audio;

/// <summary>Speech to text for the microphone button. Audio is never stored.</summary>
public interface ISpeechToText
{
    /// <summary>False when the chosen provider has no API key (the microphone button is hidden).</summary>
    bool IsConfigured { get; }

    /// <summary>The words spoken (may be empty). Throws AiProviderException when the provider fails.</summary>
    Task<string> TranscribeAsync(Stream audio, string fileName, CancellationToken cancellationToken);
}

/// <summary>The OpenAI audio API (/audio/transcriptions), with OpenAI's or Groq's endpoint and key.</summary>
public sealed class OpenAiSpeechToText : ISpeechToText
{
    private readonly AudioClient? _client;
    private readonly ILogger<OpenAiSpeechToText> _logger;
    private readonly string _name;

    /// <param name="transport">Tests only: replaces the HTTP pipeline.</param>
    public OpenAiSpeechToText(IOptions<AiOptions> options, ILogger<OpenAiSpeechToText> logger, PipelineTransport? transport = null)
    {
        _logger = logger;
        var ai = options.Value;
        var settings = ai.Transcription;
        var account = ai.ProviderSettings(settings.Provider);
        _name = $"{settings.Provider}/{settings.Model}";
        if (string.IsNullOrWhiteSpace(account.ApiKey) || string.IsNullOrWhiteSpace(settings.Model))
        {
            return;
        }

        var clientOptions = new OpenAIClientOptions { NetworkTimeout = TimeSpan.FromSeconds(ai.TimeoutSeconds) };
        if (!string.IsNullOrWhiteSpace(account.Endpoint))
        {
            clientOptions.Endpoint = new Uri(account.Endpoint);
        }
        if (transport is not null)
        {
            clientOptions.Transport = transport;
        }
        _client = new AudioClient(settings.Model, new ApiKeyCredential(account.ApiKey), clientOptions);
    }

    public bool IsConfigured => _client is not null;

    public async Task<string> TranscribeAsync(Stream audio, string fileName, CancellationToken cancellationToken)
    {
        var client = _client ?? throw new AiProviderException(AiFailureKind.NotConfigured, "Transcription is not configured.");
        try
        {
            var result = await client.TranscribeAudioAsync(audio, fileName,
                new AudioTranscriptionOptions { ResponseFormat = AudioTranscriptionFormat.Simple }, cancellationToken);
            return result.Value.Text?.Trim() ?? string.Empty;
        }
        catch (ClientResultException ex)
        {
            var kind = OpenAiChatProvider.Classify(ex);
            // The message can echo request details; log only the status.
            _logger.LogWarning("Transcription with {Model} failed with HTTP {Status} ({Kind})", _name, ex.Status, kind);
            throw new AiProviderException(kind, $"Transcription failed with HTTP {ex.Status}.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiProviderException(AiFailureKind.Timeout, "Transcription did not finish in time.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new AiProviderException(AiFailureKind.Unavailable, "Could not reach the transcription service.", ex);
        }
    }
}

/// <summary>Which audio container a recording really is, from its first bytes (never from its name or type).</summary>
public static class AudioFormats
{
    /// <summary>The file name to send (its extension tells the provider the format), or null if not audio we accept.</summary>
    public static string? FileNameFor(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }))
        {
            return "speech.webm"; // Matroska/WebM (Chrome, Edge, Firefox MediaRecorder)
        }
        if (head.StartsWith("OggS"u8))
        {
            return "speech.ogg"; // Ogg Opus (Firefox)
        }
        if (head.Length >= 12 && head[4..8].SequenceEqual("ftyp"u8))
        {
            return "speech.m4a"; // MP4 audio (Safari MediaRecorder)
        }
        if (head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WAVE"u8))
        {
            return "speech.wav";
        }
        if (head.StartsWith("ID3"u8) || (head.Length >= 2 && head[0] == 0xFF && (head[1] & 0xE0) == 0xE0))
        {
            return "speech.mp3";
        }
        return null;
    }
}
