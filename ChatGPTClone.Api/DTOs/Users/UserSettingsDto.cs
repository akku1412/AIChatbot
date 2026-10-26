using ChatGPTClone.Api.Models;

namespace ChatGPTClone.Api.DTOs.Users;

/// <summary>GET /api/users/me/settings. Custom instructions; null = not set.</summary>
public sealed record UserSettingsDto(string? AboutMe, string? ResponseStyle)
{
    public static readonly UserSettingsDto Empty = new(null, null);

    public static UserSettingsDto From(UserSettings? s) => s is null ? Empty : new(s.AboutMe, s.ResponseStyle);
}

/// <summary>PUT /api/users/me/settings. Replaces both fields; empty or whitespace clears a field.</summary>
public sealed record UpdateUserSettingsRequest(string? AboutMe, string? ResponseStyle);
