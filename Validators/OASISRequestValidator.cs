using FluentValidation;
using OASIS.WebAPI.Core;
using OASIS.WebAPI.Models.Requests;

namespace OASIS.WebAPI.Validators;

public class OASISRequestValidator : AbstractValidator<OASISRequest>
{
    public OASISRequestValidator()
    {
        RuleFor(x => x.ProviderType)
            .IsInEnum().WithMessage("ProviderType is not a valid value.");

        RuleFor(x => x.AutoLoadBalanceMode)
            .IsInEnum().WithMessage("AutoLoadBalanceMode is not a valid value.");

        RuleForEach(x => x.CustomProviderKeys)
            .NotEmpty().WithMessage("Each CustomProviderKey must not be empty.")
            .MaximumLength(128).WithMessage("Each CustomProviderKey must not exceed 128 characters.");
    }
}
