using FluentValidation;
using OASIS.WebAPI.Models.Requests;

namespace OASIS.WebAPI.Validators;

public class WalletGenerateRequestValidator : AbstractValidator<WalletGenerateRequest>
{
    public WalletGenerateRequestValidator()
    {
        RuleFor(x => x.ChainType)
            .NotEmpty().WithMessage("ChainType is required.")
            .MaximumLength(64).WithMessage("ChainType must not exceed 64 characters.");

        When(x => x.Label != null, () =>
        {
            RuleFor(x => x.Label)
                .MaximumLength(128).WithMessage("Label must not exceed 128 characters.");
        });
    }
}
