using ChatGPTClone.Api.DTOs.Projects;
using ChatGPTClone.Api.Models;
using FluentValidation;

namespace ChatGPTClone.Api.Validators;

public sealed class SaveProjectRequestValidator : AbstractValidator<SaveProjectRequest>
{
    public SaveProjectRequestValidator()
    {
        RuleFor(r => r.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Project name is required.")
            .MaximumLength(Project.NameMaxLength).WithMessage($"Project name must be {Project.NameMaxLength} characters or fewer.");
        RuleFor(r => r.Instructions)
            .MaximumLength(Project.InstructionsMaxLength)
            .WithMessage($"Project instructions must be {Project.InstructionsMaxLength} characters or fewer.");
    }
}

public sealed class AddProjectFileRequestValidator : AbstractValidator<AddProjectFileRequest>
{
    public AddProjectFileRequestValidator()
    {
        RuleFor(r => r.FileId).NotEmpty().WithMessage("fileId is required.");
    }
}
