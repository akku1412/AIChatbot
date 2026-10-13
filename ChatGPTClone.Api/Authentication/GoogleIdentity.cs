namespace ChatGPTClone.Api.Authentication;

/// <summary>Identity facts taken from a Google ID token that passed validation.</summary>
public sealed record GoogleIdentity(string GoogleId, string Email, string FullName, string? PictureUrl);
