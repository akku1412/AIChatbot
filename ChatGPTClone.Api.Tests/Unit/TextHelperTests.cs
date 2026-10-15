using ChatGPTClone.Api.Services;

namespace ChatGPTClone.Api.Tests.Unit;

public class TextHelperTests
{
    [Theory]
    [InlineData("\"Understanding JWT Refresh Tokens\"", "Understanding JWT Refresh Tokens")]
    [InlineData("Title: Redis Use Cases.", "Redis Use Cases")]
    [InlineData("**Angular Signals Plan**\nSome extra line", "Angular Signals Plan")]
    [InlineData("  # SQL Duplicates?  ", "SQL Duplicates")]
    public void CleanTitle_strips_quotes_prefixes_markdown_and_trailing_punctuation(string raw, string expected) =>
        Assert.Equal(expected, ChatService.CleanTitle(raw));

    [Fact]
    public void CleanTitle_caps_length_at_80()
    {
        Assert.Equal(80, ChatService.CleanTitle(new string('a', 200)).Length);
    }

    [Fact]
    public void FallbackTitle_keeps_short_messages_and_collapses_whitespace()
    {
        Assert.Equal("Hello there friend", ChatService.FallbackTitle("  Hello\n\nthere   friend "));
    }

    [Fact]
    public void FallbackTitle_cuts_long_messages_at_a_word_boundary()
    {
        var title = ChatService.FallbackTitle("Write a SQL Server query that finds duplicate emails in a Users table please");

        Assert.EndsWith("…", title);
        Assert.True(title.Length <= 51);
        Assert.DoesNotContain("  ", title);
    }

    [Theory]
    [InlineData("akanshajha1412@gmail.com", "ak************@gmail.com")]
    [InlineData("ab@x.io", "a***@x.io")]
    [InlineData("a@x.io", "a***@x.io")]
    [InlineData("not-an-email", "***")]
    public void MaskEmail_hides_most_of_the_local_part(string email, string expected) =>
        Assert.Equal(expected, AuthService.MaskEmail(email));

    [Theory]
    [InlineData("akansha.jha99@gmail.com", "Akansha Jha")]
    [InlineData("john_smith+ai@example.com", "John Smith Ai")]
    [InlineData("1234@example.com", "1234")]
    public void NameFromEmail_builds_a_friendly_default_name(string email, string expected) =>
        Assert.Equal(expected, AuthService.NameFromEmail(email));

    [Theory]
    [InlineData(1, "1 second")]
    [InlineData(45, "45 seconds")]
    [InlineData(60, "1 minute")]
    [InlineData(61, "2 minutes")]
    [InlineData(3600, "60 minutes")]
    public void FormatWait_is_human_readable(int seconds, string expected) =>
        Assert.Equal(expected, OtpService.FormatWait(seconds));
}
