namespace ChatGPTClone.Api.DTOs.Sharing;

/// <summary>
/// POST /api/conversations/{id}/share. The token is shown only now (the server keeps just its hash); the app builds
/// the link as {origin}/share/{token}.
/// </summary>
public sealed record ShareCreatedDto(string Token, DateTime SharedAt, int MessageCount);

/// <summary>GET /api/conversations/{id}/share: whether a link is live (the link itself can't be shown again).</summary>
public sealed record ShareStatusDto(bool IsShared, DateTime? SharedAt, int? MessageCount);

/// <summary>GET /api/share/{token} (public): the snapshot. No ids of any kind, so nothing else can be requested with it.</summary>
public sealed record SharedConversationDto(string Title, DateTime SharedAt, IReadOnlyList<SharedMessageDto> Messages);

/// <summary>One message of the snapshot. Attachments are names only: shared pages never offer file downloads.</summary>
public sealed record SharedMessageDto(string Role, string Content, DateTime CreatedAt, IReadOnlyList<string> Attachments, bool IsTruncated);
