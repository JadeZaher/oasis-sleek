using FluentValidation;
using OASIS.WebAPI.Models.Requests;

namespace OASIS.WebAPI.Validation;

public class NftMintValidator : AbstractValidator<NftMintRequest>
{
    public NftMintValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.ChainId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.WalletId).NotEqual(Guid.Empty).WithMessage("WalletId is required.");
    }
}
