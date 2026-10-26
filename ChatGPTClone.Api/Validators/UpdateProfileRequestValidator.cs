using ChatGPTClone.Api.DTOs.Users;
using ChatGPTClone.Api.Models;
using FluentValidation;

namespace ChatGPTClone.Api.Validators;

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(r => r.FullName)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("Name is required.")
            .MaximumLength(200).WithMessage("Name must be 200 characters or fewer.");
    }
}

public sealed class UpdateUserSettingsRequestValidator : AbstractValidator<UpdateUserSettingsRequest>
{
    public UpdateUserSettingsRequestValidator()
    {
        const int max = UserSettings.MaxInstructionLength;
        RuleFor(r => r.AboutMe).MaximumLength(max).WithMessage($"\"About you\" must be {max} characters or fewer.");
        RuleFor(r => r.ResponseStyle).MaximumLength(max).WithMessage($"\"How to respond\" must be {max} characters or fewer.");
        RuleFor(r => r.Theme)
            .Must(t => UserSettings.Themes.Contains(t))
            .When(r => r.Theme is not null)
            .WithMessage("Theme must be \"system\", \"light\" or \"dark\".");
    }
}
