namespace ChatGPTClone.Api.Models;

/// <summary>
/// A folder of chats with shared instructions and files. Every chat in the project gets the instructions in its
/// system prompt and searches the project's files along with its own attachments.
/// </summary>
public class Project : IAuditable
{
    public const int NameMaxLength = 100;
    public const int InstructionsMaxLength = 4000;
    public const int MaxFiles = 20;

    public int ProjectId { get; set; }

    public int UserId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Added to every chat in the project, after the app's rules and the user's custom instructions.</summary>
    public string? Instructions { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public ICollection<ProjectFile> Files { get; set; } = new List<ProjectFile>();
    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
}

/// <summary>A file shared by all chats in a project (the file itself stays the user's upload).</summary>
public class ProjectFile : IHasCreatedAt
{
    public int ProjectId { get; set; }
    public Guid FileId { get; set; }
    public DateTime CreatedAt { get; set; }

    public Project Project { get; set; } = null!;
    public UploadedFile File { get; set; } = null!;
}
