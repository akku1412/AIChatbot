using ChatGPTClone.Api.Data.Configurations;
using ChatGPTClone.Api.DTOs.Chat;
using FluentValidation;

namespace ChatGPTClone.Api.Validators;

public static class ChatLimits
{
    /// <summary>Must match MAX_MESSAGE_LENGTH in the Angular app.</summary>
    public const int MaxMessageLength = 8000;
}

public sealed class ListConversationsQueryValidator : AbstractValidator<ListConversationsQuery>
{
    public ListConversationsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 50);
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
    public CreateConversationRequestValidator()
    {
        RuleFor(r => r.Title).MaximumLength(ConversationConfiguration.TitleMaxLength);
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
    public SendMessageRequestValidator()
    {
        RuleFor(r => r.Content)
            .Must(c => !string.IsNullOrWhiteSpace(c)).WithMessage("Message cannot be empty.")
            .MaximumLength(ChatLimits.MaxMessageLength)
            .WithMessage($"Message must be {ChatLimits.MaxMessageLength} characters or fewer.");
    }
}
