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

public sealed class SendOtpRequestValidator : AbstractValidator<SendOtpRequest>
{
    public SendOtpRequestValidator()
    {
        RuleFor(r => r.ChallengeToken)
            .NotEmpty().WithMessage("Verification session is missing. Please sign in again.")
            .MaximumLength(2048);
    }
}

public sealed class VerifyOtpRequestValidator : AbstractValidator<VerifyOtpRequest>
{
    public VerifyOtpRequestValidator()
    {
        RuleFor(r => r.ChallengeToken)
            .NotEmpty().WithMessage("Verification session is missing. Please sign in again.")
            .MaximumLength(2048);

        RuleFor(r => r.Code)
            .NotEmpty().WithMessage("Enter the 6-digit code.")
            .Matches(@"^\d{6}$").WithMessage("The code must be exactly 6 digits.");
    }
}
