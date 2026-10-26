using ChatGPTClone.Api.Models;

namespace ChatGPTClone.Api.DTOs.Users;

/// <summary>GET /api/users/me/settings. Custom instructions (null = not set) and the theme ("system", "light" or "dark").</summary>
public sealed record UserSettingsDto(string? AboutMe, string? ResponseStyle, string Theme = "system")
{
    public static readonly UserSettingsDto Empty = new(null, null);

    public static UserSettingsDto From(UserSettings? s) =>
        s is null ? Empty : new(s.AboutMe, s.ResponseStyle, s.Theme ?? "system");
}

/// <summary>
/// PUT /api/users/me/settings. Each field that is present is saved; a missing (null) field is left as it is, so the
/// theme toggle and the instructions form don't overwrite each other. An empty or whitespace instruction clears it.
/// Theme: "system", "light" or "dark".
/// </summary>
public sealed record UpdateUserSettingsRequest(string? AboutMe, string? ResponseStyle, string? Theme = null);
