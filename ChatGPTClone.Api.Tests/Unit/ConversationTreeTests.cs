using ChatGPTClone.Api.Models;
using ChatGPTClone.Api.Services;

namespace ChatGPTClone.Api.Tests.Unit;

public class ConversationTreeTests
{
    private static MessageNode U(long id, long? parent, bool active = true, int version = 1) => new(id, parent, MessageRole.User, version, active);
    private static MessageNode A(long id, long? parent, bool active = true, int version = 1) => new(id, parent, MessageRole.Assistant, version, active);

    // 1 Q1 ─ 2 A1 ─ 3 Q2 ─ 4 A2 (inactive) / 6 A2' (active)
    //   └─ 5 Q1' (edit, inactive) ─ 7 A1''
    private static readonly MessageNode[] Tree =
    [
        U(1, null), A(2, 1), U(3, 2), A(4, 3, active: false), U(5, null, active: false, version: 2), A(6, 3, version: 2), A(7, 5),
    ];

    [Fact]
    public void Active_thread_follows_the_active_version_at_every_step()
    {
        var thread = ConversationTree.ActiveThread(Tree);
        Assert.Equal([1L, 2, 3, 6], thread.Select(t => t.Node.MessageId));
        Assert.Equal([(1, 2), (1, 1), (1, 1), (2, 2)], thread.Select(t => (t.VersionNumber, t.VersionCount)));
    }

    [Fact]
    public void Switching_to_another_branch_keeps_that_branch_s_own_choices()
    {
        var switched = Tree.Select(n => n.MessageId switch
        {
            1 => n with { IsActive = false },
            5 => n with { IsActive = true },
            _ => n,
        }).ToList();
        Assert.Equal([5L, 7], ConversationTree.ActiveThread(switched).Select(t => t.Node.MessageId));
    }

    [Fact]
    public void Path_siblings_and_next_version()
    {
        Assert.Equal([1L, 2, 3, 4], ConversationTree.PathTo(Tree, 4).Select(n => n.MessageId));
        Assert.Equal([4L, 6], ConversationTree.Siblings(Tree, Tree.Single(n => n.MessageId == 6)).Select(n => n.MessageId));
        Assert.Equal(3, ConversationTree.NextVersionIndex(Tree, 3));
        Assert.Equal(3, ConversationTree.NextVersionIndex(Tree, null));
        Assert.Equal(1, ConversationTree.NextVersionIndex(Tree, 7));
    }

    [Fact]
    public void Empty_and_inconsistent_data_are_handled()
    {
        Assert.Empty(ConversationTree.ActiveThread([]));
        // No active sibling at all (shouldn't happen): the newest one is shown.
        Assert.Equal([2L], ConversationTree.ActiveThread([U(1, null, active: false), U(2, null, active: false)]).Select(t => t.Node.MessageId));
    }

    [Theory]
    [InlineData("The total marketing budget for Q3 is 4.5 lakh rupees", "budget", "The total marketing budget for Q3 is 4.5 lakh rupees")]
    [InlineData("short text", "missing", "short text")]
    public void Snippets_show_the_text_around_the_match(string content, string term, string expected) =>
        Assert.Equal(expected, ConversationService.Snippet(content, term));

    [Fact]
    public void Long_snippets_are_cut_on_word_boundaries_with_ellipses()
    {
        var content = string.Join(' ', Enumerable.Range(1, 60).Select(i => $"word{i}")) + " NEEDLE " +
                      string.Join(' ', Enumerable.Range(61, 60).Select(i => $"word{i}"));
        var snippet = ConversationService.Snippet(content, "needle");
        Assert.StartsWith("…word", snippet);
        Assert.EndsWith("…", snippet);
        Assert.Contains("NEEDLE", snippet);
        Assert.True(snippet.Length <= 175);
        Assert.DoesNotContain("  ", snippet);
    }
}
