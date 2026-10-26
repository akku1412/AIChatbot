using ChatGPTClone.Api.DTOs.Files;

namespace ChatGPTClone.Api.DTOs.Projects;

/// <summary>A project in the sidebar list.</summary>
public sealed record ProjectDto(int ProjectId, string Name, string? Instructions, int FileCount, int ConversationCount,
    DateTime CreatedAt, DateTime UpdatedAt);

/// <summary>GET /api/projects/{id}: the project and its shared files.</summary>
public sealed record ProjectDetailDto(int ProjectId, string Name, string? Instructions, int ConversationCount,
    IReadOnlyList<UploadedFileDto> Files, DateTime CreatedAt, DateTime UpdatedAt);

/// <summary>POST /api/projects and PUT /api/projects/{id}. Blank instructions clear them.</summary>
public sealed record SaveProjectRequest(string Name, string? Instructions);

/// <summary>POST /api/projects/{id}/files: share one of the user's uploaded files with the project.</summary>
public sealed record AddProjectFileRequest(Guid FileId);
