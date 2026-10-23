using ChatGPTClone.Api.Models;

namespace ChatGPTClone.Api.Services;

/// <summary>The shape of one message, enough to work out branches without loading message text.</summary>
public sealed record MessageNode(long MessageId, long? ParentMessageId, MessageRole Role, int VersionIndex, bool IsActive);

/// <summary>A message on the visible thread with its version position ("2 of 3").</summary>
public sealed record ThreadNode(MessageNode Node, int VersionNumber, int VersionCount);

/// <summary>
/// Messages form a tree: each has a parent (the message it follows); regenerated replies and edited questions are
/// siblings of the version they replace. The visible thread starts at the active root and follows the active child
/// at every step. A child always has a higher id than its parent, so the thread is in id order.
/// </summary>
public static class ConversationTree
{
    /// <summary>The thread the user sees: active root, then the active child of each message.</summary>
    public static List<ThreadNode> ActiveThread(IReadOnlyCollection<MessageNode> nodes)
    {
        var children = ChildrenByParent(nodes);
        var thread = new List<ThreadNode>();
        long? parent = null;
        while (children.TryGetValue(Key(parent), out var siblings) && siblings.Count > 0)
        {
            // Normally exactly one sibling is active; fall back to the newest if data is inconsistent.
            var current = siblings.LastOrDefault(n => n.IsActive) ?? siblings[^1];
            thread.Add(new ThreadNode(current, siblings.IndexOf(current) + 1, siblings.Count));
            parent = current.MessageId;
        }
        return thread;
    }

    /// <summary>The path from the root to <paramref name="messageId"/> (inclusive), following parents.</summary>
    public static List<MessageNode> PathTo(IReadOnlyCollection<MessageNode> nodes, long messageId)
    {
        var byId = nodes.ToDictionary(n => n.MessageId);
        var path = new List<MessageNode>();
        for (long? id = messageId; id is { } current && byId.TryGetValue(current, out var node); id = node.ParentMessageId)
        {
            path.Add(node);
            if (path.Count > byId.Count)
            {
                break; // defensive: a cycle can't happen (child ids are larger), but never loop forever
            }
        }
        path.Reverse();
        return path;
    }

    /// <summary>All versions of a message (same parent), oldest first, including the message itself.</summary>
    public static List<MessageNode> Siblings(IReadOnlyCollection<MessageNode> nodes, MessageNode node) =>
        nodes.Where(n => n.ParentMessageId == node.ParentMessageId).OrderBy(n => n.MessageId).ToList();

    /// <summary>The version number a new sibling under <paramref name="parentId"/> gets.</summary>
    public static int NextVersionIndex(IReadOnlyCollection<MessageNode> nodes, long? parentId) =>
        nodes.Where(n => n.ParentMessageId == parentId).Select(n => n.VersionIndex).DefaultIfEmpty(0).Max() + 1;

    private static Dictionary<long, List<MessageNode>> ChildrenByParent(IReadOnlyCollection<MessageNode> nodes) =>
        nodes.GroupBy(n => Key(n.ParentMessageId))
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.MessageId).ToList());

    // Dictionary keys can't be null: the roots (no parent) use 0, which is never a real message id.
    private static long Key(long? parentId) => parentId ?? 0;
}
