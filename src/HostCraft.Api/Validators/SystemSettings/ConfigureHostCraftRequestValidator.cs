using FluentValidation;
using HostCraft.Api.Models.SystemSettings;

namespace HostCraft.Api.Validators.SystemSettings;

public class ConfigureHostCraftRequestValidator : AbstractValidator<ConfigureHostCraftRequest>
{
    public ConfigureHostCraftRequestValidator()
    {
        RuleFor(x => x.Domain)
            .NotEmpty();

        RuleFor(x => x.LetsEncryptEmail)
            .NotEmpty()
            .EmailAddress()
            .When(x => x.EnableHttps);
    }
}
