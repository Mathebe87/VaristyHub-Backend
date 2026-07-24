using FluentValidation;

namespace VarsityHub.Modules.Auth;

public sealed class RegisterValidator : AbstractValidator<RegisterRequest>
{
    public RegisterValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).MinimumLength(8);
        RuleFor(x => x.Channel).Must(c => c is "email" or "sms")
            .WithMessage("Channel must be 'email' or 'sms'.");
        RuleFor(x => x.Phone).NotEmpty().When(x => x.Channel == "sms")
            .WithMessage("Phone is required when Channel is 'sms'.");
    }
}

public sealed class VerifyOtpValidator : AbstractValidator<VerifyOtpRequest>
{
    public VerifyOtpValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().Length(6);
    }
}
