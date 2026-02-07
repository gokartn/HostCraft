using FluentValidation;
using HostCraft.Api.Models.SystemSettings;

namespace HostCraft.Api.Validators.SystemSettings;

public class ConfigureTraefikDashboardRequestValidator : AbstractValidator<ConfigureTraefikDashboardRequest>
{
    public ConfigureTraefikDashboardRequestValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .When(x => x.EnableAuth);

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .When(x => x.EnableAuth);
    }
}
