using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Data.Configurations;
using Microsoft.Extensions.Options;
using ChatGPTClone.Api.DTOs.Chat;
using ChatGPTClone.Api.Models;
using ChatGPTClone.Api.Services.Ai;
using FluentValidation;
using static ChatGPTClone.Api.Validators.ChatLimits;

namespace ChatGPTClone.Api.Validators;

public static class ChatLimits
{
    /// <summary>Must match MAX_MESSAGE_LENGTH in the Angular app.</summary>
    public const int MaxMessageLength = 8000;

    public const string UnknownModel = "This model isn't available. Choose one from GET /api/models.";
}

public sealed class ListConversationsQueryValidator : AbstractValidator<ListConversationsQuery>
{
    public ListConversationsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 50);
    }
}

public sealed class SearchConversationsQueryValidator : AbstractValidator<SearchConversationsQuery>
{
    public SearchConversationsQueryValidator()
    {
        RuleFor(q => q.Q)
            .Must(q => (q ?? "").Trim().Length >= 2).WithMessage("Type at least 2 characters to search.")
            .MaximumLength(100).WithMessage("Search text must be 100 characters or fewer.");
        RuleFor(q => q.Limit).InclusiveBetween(1, 50);
    }
}

public sealed class PatchConversationRequestValidator : AbstractValidator<PatchConversationRequest>
{
    public PatchConversationRequestValidator(IAiModelCatalog models)
    {
        RuleFor(r => r)
            .Must(r => r.Title is not null || r.IsPinned is not null || r.Archived is not null || r.ModelId is not null)
            .WithName("request").WithMessage("Nothing to change: send title, isPinned, archived or modelId.");
        RuleFor(r => r.ModelId).Must(models.IsAllowed).WithMessage(UnknownModel).When(r => r.ModelId is not null);
        RuleFor(r => r.Title!)
            .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("Title cannot be empty.")
            .MaximumLength(ConversationConfiguration.TitleMaxLength)
            .WithMessage($"Title must be {ConversationConfiguration.TitleMaxLength} characters or fewer.")
            .When(r => r.Title is not null);
    }
}

public sealed class EditMessageRequestValidator : AbstractValidator<EditMessageRequest>
{
    public EditMessageRequestValidator()
    {
        RuleFor(r => r.Content)
            .Must(c => !string.IsNullOrWhiteSpace(c)).WithMessage("Message cannot be empty.")
            .MaximumLength(ChatLimits.MaxMessageLength)
            .WithMessage($"Message must be {ChatLimits.MaxMessageLength} characters or fewer.");
    }
}

public sealed class SwitchVersionRequestValidator : AbstractValidator<SwitchVersionRequest>
{
    public SwitchVersionRequestValidator()
    {
        RuleFor(r => r.VersionNumber).GreaterThanOrEqualTo(1);
    }
}

public sealed class MessagesQueryValidator : AbstractValidator<MessagesQuery>
{
    public MessagesQueryValidator()
    {
        RuleFor(q => q.Limit).InclusiveBetween(1, 100);
        RuleFor(q => q.Before).GreaterThan(0).When(q => q.Before.HasValue);
    }
}

public sealed class CreateConversationRequestValidator : AbstractValidator<CreateConversationRequest>
{
    public CreateConversationRequestValidator(IAiModelCatalog models)
    {
        RuleFor(r => r.Title).MaximumLength(ConversationConfiguration.TitleMaxLength);
        RuleFor(r => r.ModelId).Must(models.IsAllowed).WithMessage(UnknownModel).When(r => r.ModelId is not null);
    }
}

public sealed class RenameConversationRequestValidator : AbstractValidator<RenameConversationRequest>
{
    public RenameConversationRequestValidator()
    {
        RuleFor(r => r.Title)
            .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("Title is required.")
            .MaximumLength(ConversationConfiguration.TitleMaxLength)
            .WithMessage($"Title must be {ConversationConfiguration.TitleMaxLength} characters or fewer.");
    }
}

public sealed class SendMessageRequestValidator : AbstractValidator<SendMessageRequest>
{
    public SendMessageRequestValidator(IOptions<FileUploadOptions> fileOptions)
    {
        var maxFiles = fileOptions.Value.MaxFilesPerMessage;

        // A message needs text, files, or both.
        RuleFor(r => r.Content)
            .Must((request, content) => !string.IsNullOrWhiteSpace(content) || request.AttachmentIds is { Count: > 0 })
            .WithMessage("Message cannot be empty.")
            .MaximumLength(ChatLimits.MaxMessageLength)
            .WithMessage($"Message must be {ChatLimits.MaxMessageLength} characters or fewer.");

        RuleFor(r => r.AttachmentIds)
            .Must(ids => ids is null || ids.Count <= maxFiles)
            .WithMessage($"You can attach at most {maxFiles} files to one message.")
            .Must(ids => ids is null || ids.All(id => id != Guid.Empty))
            .WithMessage("Invalid attachment.");
    }
}

public sealed class MessageFeedbackRequestValidator : AbstractValidator<MessageFeedbackRequest>
{
    public MessageFeedbackRequestValidator()
    {
        RuleFor(r => r.Rating).Must(r => r is "up" or "down").WithMessage("Rating must be \"up\" or \"down\".");
        RuleFor(r => r.Comment)
            .MaximumLength(MessageFeedback.CommentMaxLength)
            .WithMessage($"Comment must be {MessageFeedback.CommentMaxLength} characters or fewer.");
    }
}
