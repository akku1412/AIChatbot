namespace ChatGPTClone.Api.DTOs.Users;

/// <summary>Only the display name is editable; email and photo always come from Google.</summary>
public sealed record UpdateProfileRequest(string FullName);
