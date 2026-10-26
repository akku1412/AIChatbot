using ChatGPTClone.Api.Data;
using ChatGPTClone.Api.DTOs.Files;
using ChatGPTClone.Api.DTOs.Projects;
using ChatGPTClone.Api.Middleware;
using ChatGPTClone.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatGPTClone.Api.Services;

/// <summary>Projects: folders of chats with shared instructions and files. Every query is scoped to the owner.</summary>
public interface IProjectService
{
    Task<IReadOnlyList<ProjectDto>> ListAsync(int userId, CancellationToken cancellationToken);
    Task<ProjectDetailDto> GetAsync(int userId, int projectId, CancellationToken cancellationToken);
    Task<ProjectDto> CreateAsync(int userId, SaveProjectRequest request, CancellationToken cancellationToken);
    Task<ProjectDto> UpdateAsync(int userId, int projectId, SaveProjectRequest request, CancellationToken cancellationToken);

    /// <summary>Deletes the project. Its chats move back to the main list; its files stay in the user's uploads.</summary>
    Task DeleteAsync(int userId, int projectId, CancellationToken cancellationToken);

    Task<ProjectDetailDto> AddFileAsync(int userId, int projectId, Guid fileId, CancellationToken cancellationToken);
    Task<ProjectDetailDto> RemoveFileAsync(int userId, int projectId, Guid fileId, CancellationToken cancellationToken);

    /// <summary>404 unless the project exists and belongs to the user.</summary>
    Task EnsureOwnedAsync(int userId, int projectId, CancellationToken cancellationToken);

    /// <summary>What a chat in the project needs: its instructions and its ready files (newest first).</summary>
    Task<(string? Instructions, List<UploadedFile> Files)> GetChatContextAsync(int userId, int projectId, CancellationToken cancellationToken);
}

public sealed class ProjectService(AppDbContext db, ILogger<ProjectService> logger) : IProjectService
{
    public async Task<IReadOnlyList<ProjectDto>> ListAsync(int userId, CancellationToken cancellationToken) =>
        await db.Projects.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.Name).ThenBy(p => p.ProjectId)
            .Select(p => new ProjectDto(p.ProjectId, p.Name, p.Instructions, p.Files.Count, p.Conversations.Count, p.CreatedAt, p.UpdatedAt))
            .ToListAsync(cancellationToken);

    public async Task<ProjectDetailDto> GetAsync(int userId, int projectId, CancellationToken cancellationToken)
    {
        var project = await db.Projects.AsNoTracking()
            .Where(p => p.ProjectId == projectId && p.UserId == userId)
            .Select(p => new
            {
                p.ProjectId, p.Name, p.Instructions, p.CreatedAt, p.UpdatedAt,
                Conversations = p.Conversations.Count,
                Files = p.Files.OrderByDescending(f => f.CreatedAt).Select(f => f.File).ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw NotFound();
        return new ProjectDetailDto(project.ProjectId, project.Name, project.Instructions, project.Conversations,
            project.Files.Select(UploadedFileDto.From).ToList(), project.CreatedAt, project.UpdatedAt);
    }

    public async Task<ProjectDto> CreateAsync(int userId, SaveProjectRequest request, CancellationToken cancellationToken)
    {
        var project = new Project { UserId = userId };
        Apply(project, request);
        db.Projects.Add(project);
        await db.SaveChangesAsync(cancellationToken);
        return new ProjectDto(project.ProjectId, project.Name, project.Instructions, 0, 0, project.CreatedAt, project.UpdatedAt);
    }

    public async Task<ProjectDto> UpdateAsync(int userId, int projectId, SaveProjectRequest request, CancellationToken cancellationToken)
    {
        var project = await GetOwnedAsync(userId, projectId, cancellationToken);
        Apply(project, request);
        await db.SaveChangesAsync(cancellationToken);
        return (await ListAsync(userId, cancellationToken)).Single(p => p.ProjectId == projectId);
    }

    public async Task DeleteAsync(int userId, int projectId, CancellationToken cancellationToken)
    {
        var project = await GetOwnedAsync(userId, projectId, cancellationToken);
        // One transaction, run through the execution strategy (required with EnableRetryOnFailure).
        var moved = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            // Conversations.ProjectId is NO ACTION (SQL Server cascade-path rule): detach the chats first.
            var count = await db.Conversations.Where(c => c.ProjectId == projectId && c.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ProjectId, (int?)null), cancellationToken);
            db.Projects.Remove(project); // its ProjectFiles rows cascade; the files themselves stay
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return count;
        });
        logger.LogInformation("Project {ProjectId} deleted by user {UserId}; {Moved} chats moved to the main list", projectId, userId, moved);
    }

    public async Task<ProjectDetailDto> AddFileAsync(int userId, int projectId, Guid fileId, CancellationToken cancellationToken)
    {
        var project = await GetOwnedAsync(userId, projectId, cancellationToken);
        var file = await db.UploadedFiles.AsNoTracking().FirstOrDefaultAsync(f => f.FileId == fileId && f.UserId == userId, cancellationToken)
            // Another user's file looks exactly like a missing one.
            ?? throw new ApiException(StatusCodes.Status404NotFound, "File not found", "This file doesn't exist or has been deleted.");
        if (file.ProcessingStatus == FileProcessingStatus.Failed)
        {
            throw new ApiException(StatusCodes.Status422UnprocessableEntity, "File can't be used",
                file.StatusMessage ?? "This file couldn't be read, so it can't be added to the project.");
        }

        var links = await db.ProjectFiles.Where(pf => pf.ProjectId == projectId).Select(pf => pf.FileId).ToListAsync(cancellationToken);
        if (!links.Contains(fileId))
        {
            if (links.Count >= Project.MaxFiles)
            {
                throw new ApiException(StatusCodes.Status409Conflict, "Project is full",
                    $"A project can hold up to {Project.MaxFiles} files. Remove one to add another.");
            }
            db.ProjectFiles.Add(new ProjectFile { ProjectId = projectId, FileId = fileId });
            project.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        return await GetAsync(userId, projectId, cancellationToken);
    }

    public async Task<ProjectDetailDto> RemoveFileAsync(int userId, int projectId, Guid fileId, CancellationToken cancellationToken)
    {
        await EnsureOwnedAsync(userId, projectId, cancellationToken);
        await db.ProjectFiles.Where(pf => pf.ProjectId == projectId && pf.FileId == fileId).ExecuteDeleteAsync(cancellationToken);
        return await GetAsync(userId, projectId, cancellationToken);
    }

    public async Task EnsureOwnedAsync(int userId, int projectId, CancellationToken cancellationToken)
    {
        if (!await db.Projects.AnyAsync(p => p.ProjectId == projectId && p.UserId == userId, cancellationToken))
        {
            throw NotFound();
        }
    }

    public async Task<(string? Instructions, List<UploadedFile> Files)> GetChatContextAsync(int userId, int projectId,
        CancellationToken cancellationToken)
    {
        var instructions = await db.Projects.AsNoTracking()
            .Where(p => p.ProjectId == projectId && p.UserId == userId)
            .Select(p => p.Instructions)
            .FirstOrDefaultAsync(cancellationToken);
        // Only the owner's files that finished processing; a file still being read joins once it's ready.
        var files = await db.ProjectFiles.AsNoTracking()
            .Where(pf => pf.ProjectId == projectId && pf.Project.UserId == userId && pf.File.UserId == userId
                && pf.File.ProcessingStatus == FileProcessingStatus.Completed)
            .OrderByDescending(pf => pf.CreatedAt)
            .Select(pf => pf.File)
            .ToListAsync(cancellationToken);
        return (instructions, files);
    }

    private async Task<Project> GetOwnedAsync(int userId, int projectId, CancellationToken cancellationToken) =>
        await db.Projects.FirstOrDefaultAsync(p => p.ProjectId == projectId && p.UserId == userId, cancellationToken)
        ?? throw NotFound();

    private static void Apply(Project project, SaveProjectRequest request)
    {
        project.Name = request.Name.Trim();
        project.Instructions = string.IsNullOrWhiteSpace(request.Instructions) ? null : request.Instructions.Trim();
    }

    private static ApiException NotFound() =>
        new(StatusCodes.Status404NotFound, "Project not found", "This project doesn't exist or has been deleted.");
}
