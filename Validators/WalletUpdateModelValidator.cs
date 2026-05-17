using FluentValidation;
using OASIS.WebAPI.Models.Requests;

namespace OASIS.WebAPI.Validators;

public class WalletUpdateModelValidator : AbstractValidator<WalletUpdateModel>
{
    public WalletUpdateModelValidator()
    {
        When(x => x.Label != null, () =>
        {
            RuleFor(x => x.Label)
                .MaximumLength(128).WithMessage("Label must not exceed 128 characters.");
        });
    }
}
