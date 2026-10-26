using System.Globalization;

namespace ChatGPTClone.Api.Services.Export;

/// <summary>
/// The time zone the reader is in, for times written into exported files. Times are stored in UTC; the browser sends its
/// IANA zone (e.g. "Asia/Kolkata"). A missing or unknown zone means UTC.
/// </summary>
public sealed class UserTimeZone
{
    public const int MaxIdLength = 64;

    public static readonly UserTimeZone Utc = new(TimeZoneInfo.Utc, "UTC");

    private UserTimeZone(TimeZoneInfo zone, string id)
    {
        Zone = zone;
        Id = id;
    }

    public TimeZoneInfo Zone { get; }

    /// <summary>The IANA id as the browser sent it, or "UTC".</summary>
    public string Id { get; }

    public bool IsUtc => Zone.BaseUtcOffset == TimeSpan.Zero && !Zone.SupportsDaylightSavingTime;

    public static UserTimeZone From(string? ianaId)
    {
        var id = ianaId?.Trim();
        if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength || id.Equals("UTC", StringComparison.OrdinalIgnoreCase))
        {
            return Utc;
        }
        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone)
            // Windows without ICU only knows Windows ids.
            || (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId)
                && TimeZoneInfo.TryFindSystemTimeZoneById(windowsId, out zone)))
        {
            return new UserTimeZone(zone, id);
        }
        return Utc;
    }

    public DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    /// <summary>"2026-10-26 17:14 UTC+05:30" (the offset of that moment, so daylight saving is right), or "… UTC".</summary>
    public string Format(DateTime utc) =>
        $"{ToLocal(utc).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} {OffsetLabel(utc)}";

    /// <summary>"Asia/Kolkata (UTC+05:30)", or "UTC": said once at the top of an export.</summary>
    public string Describe(DateTime utc) => IsUtc ? "UTC" : $"{Id} ({OffsetLabel(utc)})";

    private string OffsetLabel(DateTime utc)
    {
        if (IsUtc)
        {
            return "UTC";
        }
        var offset = Zone.GetUtcOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        return $"UTC{(offset < TimeSpan.Zero ? "-" : "+")}{offset.Duration():hh\\:mm}";
    }
}
