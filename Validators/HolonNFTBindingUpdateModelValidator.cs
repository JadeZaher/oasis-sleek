using FluentValidation;
using OASIS.WebAPI.Models.Requests;

namespace OASIS.WebAPI.Validators;

public class HolonNFTBindingUpdateModelValidator : AbstractValidator<HolonNFTBindingUpdateModel>
{
    public HolonNFTBindingUpdateModelValidator()
    {
        When(x => x.Role != null, () =>
        {
            RuleFor(x => x.Role)
                .NotEmpty().WithMessage("Role must not be empty when provided.")
                .MaximumLength(64).WithMessage("Role must not exceed 64 characters.");
        });

        When(x => x.PermissionLevel != null, () =>
        {
            RuleFor(x => x.PermissionLevel)
                .NotEmpty().WithMessage("PermissionLevel must not be empty when provided.")
                .MaximumLength(64).WithMessage("PermissionLevel must not exceed 64 characters.");
        });
    }
}
