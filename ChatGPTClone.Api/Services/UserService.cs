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

    private async Task<User> LoadAsync(int userId, CancellationToken cancellationToken) =>
        await userRepository.GetByIdAsync(userId, cancellationToken)
        // A valid token for a user that no longer exists: treat as signed out.
        ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Not signed in", "Please sign in again.");
}
