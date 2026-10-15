using ChatGPTClone.Api.Models;

namespace ChatGPTClone.Api.DTOs.Users;

public sealed record UserDto(
    int UserId,
    string FullName,
    string Email,
    string? ProfileImageUrl,
    bool IsEmailVerified,
    DateTime CreatedAt)
{
    public static UserDto From(User user) =>
        new(user.UserId, user.FullName, user.Email, user.ProfileImageUrl, user.IsEmailVerified, user.CreatedAt);
}
