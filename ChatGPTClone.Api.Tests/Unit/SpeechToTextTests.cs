using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Services.Audio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChatGPTClone.Api.Tests.Unit;

public class SpeechToTextTests
{
    private sealed class FakeAudioApi(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string RequestBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = Encoding.Latin1.GetString(await request.Content!.ReadAsByteArrayAsync(cancellationToken));
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };
        }
    }

    private static OpenAiSpeechToText Client(FakeAudioApi api, string? key = "gsk_test") => new(
        Options.Create(new AiOptions
        {
            Groq = new OpenAiProviderOptions { ApiKey = key ?? "", Endpoint = "https://api.groq.test/openai/v1", Model = "chat" },
            Transcription = new TranscriptionOptions { Provider = "Groq", Model = "whisper-large-v3-turbo" },
        }),
        NullLogger<OpenAiSpeechToText>.Instance,
        new HttpClientPipelineTransport(new HttpClient(api)));

    [Fact]
    public async Task Sends_the_recording_to_the_providers_audio_endpoint_with_the_model()
    {
        var api = new FakeAudioApi(HttpStatusCode.OK, "  Book a table for two.  ");

        var text = await Client(api).TranscribeAsync(new MemoryStream([0x1A, 0x45, 0xDF, 0xA3, 1, 2, 3]), "speech.webm", CancellationToken.None);

        Assert.Equal("Book a table for two.", text);
        Assert.Equal("https://api.groq.test/openai/v1/audio/transcriptions", api.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer", api.Request.Headers.Authorization!.Scheme);
        Assert.Contains("whisper-large-v3-turbo", api.RequestBody);
        Assert.Contains("speech.webm", api.RequestBody);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, AiFailureKind.InvalidCredentials)]
    [InlineData(HttpStatusCode.BadRequest, AiFailureKind.Unavailable)]
    public async Task Provider_errors_become_AiProviderException(HttpStatusCode status, AiFailureKind kind)
    {
        var ex = await Assert.ThrowsAsync<AiProviderException>(() =>
            Client(new FakeAudioApi(status, """{"error":{"message":"nope"}}""")).TranscribeAsync(new MemoryStream([1]), "speech.webm", CancellationToken.None));
        Assert.Equal(kind, ex.Kind);
    }

    [Fact]
    public void Without_a_key_for_the_chosen_provider_it_is_not_configured()
    {
        Assert.False(Client(new FakeAudioApi(HttpStatusCode.OK, ""), key: null).IsConfigured);
    }

    [Theory]
    [InlineData(new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0, 0, 0, 0, 0, 0, 0, 0 }, "speech.webm")]
    [InlineData(new byte[] { (byte)'O', (byte)'g', (byte)'g', (byte)'S', 0, 0, 0, 0, 0, 0, 0, 0 }, "speech.ogg")]
    [InlineData(new byte[] { 0, 0, 0, 0x20, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'M', (byte)'4', (byte)'A', (byte)' ' }, "speech.m4a")]
    [InlineData(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'A', (byte)'V', (byte)'E' }, "speech.wav")]
    [InlineData(new byte[] { (byte)'I', (byte)'D', (byte)'3', 4, 0, 0, 0, 0, 0, 0, 0, 0 }, "speech.mp3")]
    [InlineData(new byte[] { (byte)'%', (byte)'P', (byte)'D', (byte)'F', 0, 0, 0, 0, 0, 0, 0, 0 }, null)]
    [InlineData(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'A', (byte)'V', (byte)'I', (byte)' ' }, null)]
    public void Audio_formats_are_recognised_from_their_bytes(byte[] head, string? expected)
    {
        Assert.Equal(expected, AudioFormats.FileNameFor(head));
    }
}
