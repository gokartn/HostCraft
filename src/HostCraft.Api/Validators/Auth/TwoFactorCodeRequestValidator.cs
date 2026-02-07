using FluentValidation;
using HostCraft.Api.Models.Auth;

namespace HostCraft.Api.Validators.Auth;

public class TwoFactorCodeRequestValidator : AbstractValidator<TwoFactorCodeRequest>
{
    public TwoFactorCodeRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .Length(6);
    }
}
