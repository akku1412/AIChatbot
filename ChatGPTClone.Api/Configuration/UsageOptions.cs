using System.ComponentModel.DataAnnotations;

namespace ChatGPTClone.Api.Configuration;

/// <summary>Bound from "Usage": per-user daily quotas (UTC days) and the prices behind the admin cost estimate.</summary>
public sealed class UsageOptions
{
    public const string SectionName = "Usage";

    /// <summary>AI tokens (input + output) each user may use per day; 0 = unlimited. Checked before every message.</summary>
    [Range(0, long.MaxValue)]
    public long DailyTokenQuota { get; init; } = 200_000;

    /// <summary>Files each user may upload per day; 0 = unlimited.</summary>
    [Range(0, int.MaxValue)]
    public int DailyUploadQuota { get; init; } = 50;

    /// <summary>US dollars per million input tokens, for the admin panel's estimated cost (set to your provider's price).</summary>
    [Range(0, 1000)]
    public decimal InputTokenPricePerMillion { get; init; } = 0.15m;

    [Range(0, 1000)]
    public decimal OutputTokenPricePerMillion { get; init; } = 0.60m;
}

/// <summary>Bound from "Admin".</summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>
    /// Accounts made admins when they sign in (how the first admin is created). Removing an address here does not
    /// demote it; change the Role column for that.
    /// </summary>
    public string[] Emails { get; init; } = [];
}
