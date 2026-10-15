using ChatGPTClone.Api.DTOs.Auth;
using FluentValidation;

namespace ChatGPTClone.Api.Validators;

public sealed class GoogleLoginRequestValidator : AbstractValidator<GoogleLoginRequest>
{
    public GoogleLoginRequestValidator()
    {
        RuleFor(r => r.IdToken)
            .NotEmpty().WithMessage("Google credential is required.")
            .MaximumLength(4096).WithMessage("Google credential is too long.");
    }
}

public sealed class EmailOtpRequestValidator : AbstractValidator<EmailOtpRequest>
{
    public EmailOtpRequestValidator()
    {
        RuleFor(r => r.Email).ValidEmail();
    }
}

public sealed class VerifyOtpRequestValidator : AbstractValidator<VerifyOtpRequest>
{
    public VerifyOtpRequestValidator()
    {
        RuleFor(r => r.Email).ValidEmail();

        RuleFor(r => r.Code)
            .NotEmpty().WithMessage("Enter the 6-digit code.")
            .Matches(@"^\d{6}$").WithMessage("The code must be exactly 6 digits.");
    }
}

internal static class EmailRules
{
    /// <summary>Required, at most 256 characters, a plausible address with a dotted domain (e.g. name@gmail.com).</summary>
    public static IRuleBuilderOptions<T, string> ValidEmail<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .Must(e => !string.IsNullOrWhiteSpace(e)).WithMessage("Enter your email address.")
            .MaximumLength(256).WithMessage("Email address is too long.")
            .Matches(@"^\s*[^@\s]+@[^@\s]+\.[^@\s]+\s*$").WithMessage("Enter a valid email address.");
}
