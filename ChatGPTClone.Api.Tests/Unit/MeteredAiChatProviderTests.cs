using System.Runtime.CompilerServices;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Models;
using ChatGPTClone.Api.Services.Usage;

namespace ChatGPTClone.Api.Tests.Unit;

/// <summary>Every AI call made for a user is added to their usage: reported tokens, or an estimate.</summary>
public class MeteredAiChatProviderTests
{
    private sealed class Scripted(AiUsage? usage, params string[] pieces) : IAiChatProvider
    {
        public string Name => "Scripted";

        public Task<AiCompletion> CompleteAsync(IReadOnlyList<AiChatMessage> messages, int maxOutputTokens, CancellationToken cancellationToken) =>
            Task.FromResult(new AiCompletion(string.Concat(pieces), null, false, usage));

        public async IAsyncEnumerable<AiStreamUpdate> StreamAsync(IReadOnlyList<AiChatMessage> messages, int maxOutputTokens,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var piece in pieces)
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                yield return new AiStreamUpdate(piece);
            }
            yield return new AiStreamUpdate("", Usage: usage);
        }
    }

    private static readonly IReadOnlyList<AiChatMessage> Prompt = [new(MessageRole.User, new string('q', 40))];

    private static (MeteredAiChatProvider Provider, List<AiUsage> Recorded) Metered(IAiChatProvider inner)
    {
        var recorded = new List<AiUsage>();
        return (new MeteredAiChatProvider(inner, u => { recorded.Add(u); return Task.CompletedTask; }), recorded);
    }

    [Fact]
    public async Task Completions_record_the_tokens_the_provider_reported()
    {
        var (provider, recorded) = Metered(new Scripted(new AiUsage(120, 30), "answer"));

        await provider.CompleteAsync(Prompt, 100, CancellationToken.None);

        Assert.Equal([new AiUsage(120, 30)], recorded);
    }

    [Fact]
    public async Task Without_reported_usage_the_tokens_are_estimated()
    {
        var (provider, recorded) = Metered(new Scripted(null, new string('a', 80)));

        await provider.CompleteAsync(Prompt, 100, CancellationToken.None);

        Assert.Equal([new AiUsage(10, 20)], recorded); // 40 / 4 and 80 / 4
    }

    [Fact]
    public async Task A_finished_stream_records_once_with_the_final_usage()
    {
        var (provider, recorded) = Metered(new Scripted(new AiUsage(50, 7), "Hel", "lo"));

        var text = "";
        await foreach (var update in provider.StreamAsync(Prompt, 100, CancellationToken.None))
        {
            text += update.Text;
        }

        Assert.Equal("Hello", text);
        Assert.Equal([new AiUsage(50, 7)], recorded);
    }

    [Fact]
    public async Task A_stopped_stream_still_records_what_was_generated()
    {
        var (provider, recorded) = Metered(new Scripted(new AiUsage(50, 7), "abcd", "efgh", "never"));
        using var stop = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var update in provider.StreamAsync(Prompt, 100, stop.Token))
            {
                if (update.Text == "efgh")
                {
                    stop.Cancel();
                }
            }
        });

        Assert.Equal([new AiUsage(10, 2)], recorded); // estimated from "abcdefgh"
    }

    [Fact]
    public void Images_count_towards_the_input_estimate()
    {
        var prompt = new List<AiChatMessage> { new(MessageRole.User, "abcd", [new AiImage([1], "image/png")]) };

        Assert.Equal(1 + TokenEstimate.PerImage, TokenEstimate.For(prompt, "").InputTokens);
    }
}
