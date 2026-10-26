using ChatGPTClone.Api.DTOs.Users;
using ChatGPTClone.Api.Interfaces;
using ChatGPTClone.Api.Middleware;
using ChatGPTClone.Api.Models;

namespace ChatGPTClone.Api.Services;

public sealed class UserService(IUserRepository userRepository) : IUserService
{
    public async Task<UserDto> GetAsync(int userId, CancellationToken cancellationToken) =>
        UserDto.From(await LoadAsync(userId, cancellationToken));

    public async Task<UserDto> UpdateProfileAsync(int userId, string fullName, CancellationToken cancellationToken)
    {
        var user = await LoadAsync(userId, cancellationToken);
        user.FullName = fullName.Trim();
        await userRepository.SaveChangesAsync(cancellationToken);
        return UserDto.From(user);
    }

    public async Task<UserSettingsDto> GetSettingsAsync(int userId, CancellationToken cancellationToken) =>
        UserSettingsDto.From(await userRepository.GetSettingsAsync(userId, cancellationToken));

    public async Task<UserSettingsDto> UpdateSettingsAsync(int userId, UpdateUserSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await userRepository.GetSettingsAsync(userId, cancellationToken);
        if (settings is null)
        {
            await LoadAsync(userId, cancellationToken); // the account must still exist
            settings = new UserSettings { UserId = userId };
            userRepository.AddSettings(settings);
        }
        settings.AboutMe = Clean(request.AboutMe);
        settings.ResponseStyle = Clean(request.ResponseStyle);
        await userRepository.SaveChangesAsync(cancellationToken);
        return UserSettingsDto.From(settings);
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private async Task<User> LoadAsync(int userId, CancellationToken cancellationToken) =>
        await userRepository.GetByIdAsync(userId, cancellationToken)
        // A valid token for a user that no longer exists: treat as signed out.
        ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Not signed in", "Please sign in again.");
}
