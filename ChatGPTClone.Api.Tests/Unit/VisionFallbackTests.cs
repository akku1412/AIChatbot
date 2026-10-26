using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Models;
using ChatGPTClone.Api.Services.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChatGPTClone.Api.Tests.Unit;

/// <summary>
/// The vision model refusing an image must not fail the reply: the provider retries once with the main model,
/// without the images and with a note so the model doesn't claim to see them. Other failures are not retried.
/// </summary>
public class VisionFallbackTests
{
    private static readonly AiChatMessage[] Prompt =
    [
        new(MessageRole.System, "You are AIChat."),
        new(MessageRole.User, "What does this screenshot say?", [new AiImage([1, 2, 3], "image/png")]),
    ];

    /// <summary>Answers the vision model with <see cref="VisionStatus"/>; the text model always succeeds.</summary>
    private sealed class FakeApi : HttpMessageHandler
    {
        public HttpStatusCode VisionStatus { get; init; } = HttpStatusCode.BadRequest;
        public string VisionError { get; init; } = "Invalid image.";
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Bodies.Add(body);
            if (body.Contains("\"vision-model\""))
            {
                var error = $$$"""{"error":{"message":"{{{VisionError}}}","type":"invalid_request_error"}}""";
                return new HttpResponseMessage(VisionStatus) { Content = new StringContent(error, Encoding.UTF8, "application/json") };
            }
            if (body.Contains("\"stream\":true"))
            {
                const string chunk = """{"id":"c","object":"chat.completion.chunk","created":1,"model":"text-model","choices":[{"index":0,"delta":{"content":"{0}"},"finish_reason":{1}}]}""";
                var sse = $"data: {chunk.Replace("{0}", "It says").Replace("{1}", "null")}\n\n" +
                          $"data: {chunk.Replace("{0}", " hello.").Replace("{1}", "\"stop\"")}\n\n" +
                          "data: [DONE]\n\n";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") };
            }
            const string completion = """{"id":"c","object":"chat.completion","created":1,"model":"text-model","choices":[{"index":0,"message":{"role":"assistant","content":"It says hello."},"finish_reason":"stop"}],"usage":{"prompt_tokens":5,"completion_tokens":3,"total_tokens":8}}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(completion, Encoding.UTF8, "application/json") };
        }
    }

    private static OpenAiChatProvider Provider(FakeApi api) => new(
        "Test",
        new OpenAiProviderOptions { ApiKey = "test-key", Model = "text-model", VisionModel = "vision-model", Endpoint = "https://ai.test/v1" },
        30,
        NullLogger<OpenAiChatProvider>.Instance,
        new HttpClientPipelineTransport(new HttpClient(api)));

    private static void AssertRetriedWithoutImages(FakeApi api)
    {
        Assert.Equal(2, api.Bodies.Count);
        Assert.Contains("\"vision-model\"", api.Bodies[0]);
        Assert.Contains("image_url", api.Bodies[0]);
        Assert.Contains("\"text-model\"", api.Bodies[1]);
        Assert.DoesNotContain("image_url", api.Bodies[1]);
        Assert.Contains("could not be shown to you", api.Bodies[1]);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task Rejected_image_is_answered_by_the_main_model_without_it(HttpStatusCode status)
    {
        var api = new FakeApi { VisionStatus = status };

        var reply = await Provider(api).CompleteAsync(Prompt, 500, CancellationToken.None);

        Assert.Equal("It says hello.", reply.Content);
        AssertRetriedWithoutImages(api);
    }

    [Fact]
    public async Task Rejected_image_falls_back_while_streaming_too()
    {
        var api = new FakeApi();

        var text = new StringBuilder();
        await foreach (var update in Provider(api).StreamAsync(Prompt, 500, CancellationToken.None))
        {
            text.Append(update.Text);
        }

        Assert.Equal("It says hello.", text.ToString());
        AssertRetriedWithoutImages(api);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Invalid API key.", AiFailureKind.InvalidCredentials)]
    [InlineData(HttpStatusCode.BadRequest, "This model's maximum context length is 8192 tokens.", AiFailureKind.ContextTooLong)]
    public async Task Other_failures_are_reported_not_retried(HttpStatusCode status, string error, AiFailureKind expected)
    {
        var api = new FakeApi { VisionStatus = status, VisionError = error };

        var ex = await Assert.ThrowsAsync<AiProviderException>(() => Provider(api).CompleteAsync(Prompt, 500, CancellationToken.None));

        Assert.Equal(expected, ex.Kind);
        Assert.Single(api.Bodies);
    }

    [Fact]
    public void Without_a_vision_model_the_prompt_says_the_images_are_not_visible()
    {
        var messages = OpenAiChatProvider.ToChatMessages(Prompt, withImages: false);

        var user = Assert.IsType<OpenAI.Chat.UserChatMessage>(messages[1]);
        Assert.Contains(OpenAiChatProvider.ImagesNotVisibleNote, user.Content[0].Text);
    }
}
