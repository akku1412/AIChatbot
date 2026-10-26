using ChatGPTClone.Api.Services.Export;

namespace ChatGPTClone.Api.Tests.Unit;

/// <summary>Roadmap feature 23: stored UTC times written in the browser's IANA time zone.</summary>
public class UserTimeZoneTests
{
    private static readonly DateTime Utc = new(2026, 10, 26, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Converts_to_the_zone_and_names_the_offset()
    {
        var india = UserTimeZone.From("Asia/Kolkata");

        Assert.Equal("2026-10-27 01:30 UTC+05:30", india.Format(Utc)); // the local date can differ from the UTC date
        Assert.Equal("Asia/Kolkata (UTC+05:30)", india.Describe(Utc));
        Assert.Equal(new DateTime(2026, 10, 27, 1, 30, 0), india.ToLocal(Utc));
    }

    [Fact]
    public void Daylight_saving_uses_the_offset_of_that_moment()
    {
        var newYork = UserTimeZone.From("America/New_York");

        Assert.Equal("2026-01-15 07:00 UTC-05:00", newYork.Format(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc)));
        Assert.Equal("2026-07-15 08:00 UTC-04:00", newYork.Format(new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("UTC")]
    [InlineData("Not/AZone")]
    [InlineData("../../windows/system32")]
    public void Missing_or_unknown_zones_fall_back_to_utc(string? id)
    {
        var zone = UserTimeZone.From(id);

        Assert.Same(UserTimeZone.Utc, zone);
        Assert.Equal("2026-10-26 20:00 UTC", zone.Format(Utc));
        Assert.Equal("UTC", zone.Describe(Utc));
    }

    [Fact]
    public void Overlong_ids_are_ignored()
    {
        Assert.Same(UserTimeZone.Utc, UserTimeZone.From(new string('A', UserTimeZone.MaxIdLength + 1)));
    }

    [Fact]
    public void Etc_zones_with_an_offset_keep_it()
    {
        Assert.Equal("2026-10-26 22:00 UTC+02:00", UserTimeZone.From("Etc/GMT-2").Format(Utc));
    }
}
