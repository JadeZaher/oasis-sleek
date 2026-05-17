using FluentValidation;
using OASIS.WebAPI.Models.Requests;

namespace OASIS.WebAPI.Validators;

public class WalletQueryRequestValidator : AbstractValidator<WalletQueryRequest>
{
    public WalletQueryRequestValidator()
    {
        When(x => x.AvatarId.HasValue, () =>
        {
            RuleFor(x => x.AvatarId!.Value)
                .NotEqual(Guid.Empty).WithMessage("AvatarId must not be an empty GUID.");
        });

        When(x => x.ChainType != null, () =>
        {
            RuleFor(x => x.ChainType)
                .NotEmpty().WithMessage("ChainType must not be empty when provided.")
                .MaximumLength(64).WithMessage("ChainType must not exceed 64 characters.");
        });
    }
}
