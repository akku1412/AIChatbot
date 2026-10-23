using System.Text;
using ChatGPTClone.Api.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ChatGPTClone.Api.Services;

/// <summary>
/// Streams one prepared chat turn to the browser as Server-Sent Events and saves it — shared by send, regenerate
/// and edit. Events: "delta" {text} while generating, then "done" (StreamTurnResult) or "error" (ProblemDetails fields).
/// </summary>
public sealed class ChatStreamResponder(IChatService chat, IOptions<JsonOptions> jsonOptions, ILogger<ChatStreamResponder> logger)
{
    public async Task StreamAsync(HttpResponse response, ChatTurn turn, GenerationHandle generation, CancellationToken requestAborted)
    {
        using var stopOrAbort = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, generation.StopToken);
        var events = new ServerSentEventWriter(response, jsonOptions.Value.JsonSerializerOptions);
        var reply = new StringBuilder();
        int? totalTokens = null;
        var truncated = false;

        try
        {
            await foreach (var update in chat.StreamReplyAsync(turn, stopOrAbort.Token))
            {
                totalTokens = update.TotalTokens ?? totalTokens;
                truncated |= update.Truncated;
                if (update.Text.Length > 0)
                {
                    reply.Append(update.Text);
                    await events.WriteAsync("delta", new { text = update.Text }, requestAborted);
                }
            }
        }
        catch (OperationCanceledException) when (stopOrAbort.IsCancellationRequested)
        {
            // Stop button, or the browser aborted the request: keep the question and whatever was generated so far
            // (marked IsTruncated). Saved with CancellationToken.None so it survives the aborted request.
            var stopped = await chat.SaveTurnAsync(turn, reply.ToString(), null, stopped: true, truncated, CancellationToken.None);
            if (!requestAborted.IsCancellationRequested)
            {
                await events.WriteAsync("done", stopped, CancellationToken.None);
            }
            return;
        }
        catch (ApiException ex) when (events.Started)
        {
            // The AI failed part-way: nothing is saved (like a failed non-streaming send); the client restores the question.
            await events.WriteAsync("error", Problem(ex), CancellationToken.None);
            return;
        }
        catch (Exception ex) when (events.Started && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Streaming a reply in conversation {ConversationId} failed", turn.Conversation.ConversationId);
            await events.WriteAsync("error",
                new { status = 500, title = "Something went wrong", detail = "The reply couldn't be completed. Please try again." },
                CancellationToken.None);
            return;
        }

        // Saved even if the browser disconnects right now, so the reply is in the history next time.
        var result = await chat.SaveTurnAsync(turn, reply.ToString(), totalTokens, stopped: false, truncated, CancellationToken.None);
        if (!requestAborted.IsCancellationRequested)
        {
            await events.WriteAsync("done", result, CancellationToken.None);
        }
    }

    private static Dictionary<string, object?> Problem(ApiException ex)
    {
        var problem = new Dictionary<string, object?> { ["status"] = ex.StatusCode, ["title"] = ex.Title, ["detail"] = ex.Message };
        foreach (var (key, value) in ex.Extensions)
        {
            problem[key] = value;
        }
        return problem;
    }
}
