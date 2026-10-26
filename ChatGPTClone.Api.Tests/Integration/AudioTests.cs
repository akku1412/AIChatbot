using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ChatGPTClone.Api.Interfaces;
using static ChatGPTClone.Api.Tests.Integration.TestAuth;

namespace ChatGPTClone.Api.Tests.Integration;

/// <summary>Roadmap feature 17: voice input (POST /api/audio/transcribe).</summary>
[Collection(ApiCollection.Name)]
public sealed class AudioTests(ApiFactory factory) : IDisposable
{
    public void Dispose() => factory.Speech.Reset();

    /// <summary>The first bytes of a WebM file (what Chrome's MediaRecorder produces), padded.</summary>
    private static readonly byte[] WebM = [0x1A, 0x45, 0xDF, 0xA3, .. new byte[2000]];

    /// <summary>An MP4 audio header ("....ftypM4A "), what Safari records.</summary>
    private static readonly byte[] M4A = [0, 0, 0, 0x20, .. "ftypM4A "u8.ToArray(), .. new byte[2000]];

    private async Task<(HttpClient Client, string Token)> SignedInAsync()
    {
        var client = factory.CreateBrowserClient();
        return (client, await SignInAsync(factory, client));
    }

    private static Task<HttpResponseMessage> TranscribeAsync(HttpClient client, string? token, byte[] bytes,
        string fileName = "recording.webm", string contentType = "audio/webm")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "audio", fileName);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/audio/transcribe") { Content = form };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client.SendAsync(request);
    }

    [Fact]
    public async Task A_recording_comes_back_as_text_and_its_real_format_is_sent_on()
    {
        var (client, token) = await SignedInAsync();

        var webm = await TranscribeAsync(client, token, WebM);
        var m4a = await TranscribeAsync(client, token, M4A, "voice.bin", "application/octet-stream"); // name/type don't matter

        Assert.Equal(HttpStatusCode.OK, webm.StatusCode);
        Assert.Equal("hello from the microphone", (await webm.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("text").GetString());
        Assert.Equal(HttpStatusCode.OK, m4a.StatusCode);
        Assert.Equal([("speech.webm", WebM.Length), ("speech.m4a", M4A.Length)], factory.Speech.Calls);
    }

    [Fact]
    public async Task Non_audio_is_refused_whatever_it_is_called()
    {
        var (client, token) = await SignedInAsync();

        var pdf = await TranscribeAsync(client, token, "%PDF-1.7 not audio"u8.ToArray(), "speech.webm", "audio/webm");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, pdf.StatusCode);
        Assert.Empty(factory.Speech.Calls);
    }

    [Fact]
    public async Task Empty_and_oversized_recordings_are_refused()
    {
        var (client, token) = await SignedInAsync();

        var empty = await TranscribeAsync(client, token, []);
        var tooBig = await TranscribeAsync(client, token, [0x1A, 0x45, 0xDF, 0xA3, .. new byte[10 * 1024 * 1024]]);

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooBig.StatusCode);
        Assert.Empty(factory.Speech.Calls);
    }

    [Fact]
    public async Task Silence_returns_422_and_provider_failures_map_like_chat_errors()
    {
        var (client, token) = await SignedInAsync();

        factory.Speech.Text = "   ";
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await TranscribeAsync(client, token, WebM)).StatusCode);

        factory.Speech.FailWith = AiFailureKind.RateLimited;
        Assert.Equal(HttpStatusCode.TooManyRequests, (await TranscribeAsync(client, token, WebM)).StatusCode);

        factory.Speech.FailWith = AiFailureKind.Timeout;
        Assert.Equal(HttpStatusCode.GatewayTimeout, (await TranscribeAsync(client, token, WebM)).StatusCode);
    }

    [Fact]
    public async Task Without_speech_to_text_the_microphone_is_hidden_and_requests_get_503()
    {
        var (client, token) = await SignedInAsync();
        factory.Speech.IsConfigured = false;

        var models = await (await client.SendAsync(Authorized(HttpMethod.Get, "/api/models", token))).Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.False(models.GetProperty("voiceInputAvailable").GetBoolean());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await TranscribeAsync(client, token, WebM)).StatusCode);

        factory.Speech.IsConfigured = true;
        var available = await (await client.SendAsync(Authorized(HttpMethod.Get, "/api/models", token))).Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.True(available.GetProperty("voiceInputAvailable").GetBoolean());
    }

    [Fact]
    public async Task Transcription_needs_sign_in()
    {
        var response = await TranscribeAsync(factory.CreateBrowserClient(), token: null, WebM);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
