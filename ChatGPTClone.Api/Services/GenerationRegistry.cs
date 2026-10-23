using System.Collections.Concurrent;
using ChatGPTClone.Api.Middleware;

namespace ChatGPTClone.Api.Services;

/// <summary>
/// Replies being generated right now, so "Stop generating" (a separate request) can cancel one.
/// Keyed by user and conversation: a user can only ever stop their own generation, and only one reply
/// per conversation runs at a time. In-memory, so with several API instances a stop only reaches the instance
/// running the reply; closing the stream from the browser also stops it on any instance.
/// </summary>
public interface IGenerationRegistry
{
    /// <summary>Registers a generation; 409 if this conversation already has one running. Dispose when finished.</summary>
    GenerationHandle Start(int userId, int conversationId);

    /// <summary>Cancels the running generation; false when nothing is running for this user and conversation.</summary>
    bool Stop(int userId, int conversationId);
}

public sealed class GenerationHandle(Action release, CancellationTokenSource source) : IDisposable
{
    /// <summary>Cancelled when the user presses Stop.</summary>
    public CancellationToken StopToken => source.Token;

    public void Dispose() => release();
}

public sealed class GenerationRegistry : IGenerationRegistry
{
    private readonly ConcurrentDictionary<(int UserId, int ConversationId), CancellationTokenSource> _running = new();

    public GenerationHandle Start(int userId, int conversationId)
    {
        var key = (userId, conversationId);
        var source = new CancellationTokenSource();
        if (!_running.TryAdd(key, source))
        {
            source.Dispose();
            throw new ApiException(StatusCodes.Status409Conflict, "Reply in progress",
                "A reply is already being generated in this conversation. Wait for it or stop it first.",
                new Dictionary<string, object?> { ["reason"] = "generation_in_progress" });
        }

        return new GenerationHandle(() =>
        {
            // Remove only our own entry, then release it.
            _running.TryRemove(new KeyValuePair<(int, int), CancellationTokenSource>(key, source));
            source.Dispose();
        }, source);
    }

    public bool Stop(int userId, int conversationId)
    {
        if (!_running.TryGetValue((userId, conversationId), out var source))
        {
            return false;
        }
        try
        {
            source.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false; // finished at the same moment
        }
    }
}
