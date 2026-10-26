namespace ChatGPTClone.Api.Models;

/// <summary>
/// One user's usage on one UTC day: what the quotas are checked against and what the admin usage report adds up.
/// Rows are only ever incremented (UsageService), never edited.
/// </summary>
public class UsageDaily
{
    public int UserId { get; set; }

    /// <summary>The UTC day (time part is always midnight).</summary>
    public DateTime Date { get; set; }

    /// <summary>Chat turns saved (sent, regenerated or edited).</summary>
    public int MessageCount { get; set; }

    /// <summary>Prompt tokens of every AI call made for the user (replies, titles, web search queries).</summary>
    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    public int UploadCount { get; set; }

    public long UploadBytes { get; set; }

    public User User { get; set; } = null!;
}
