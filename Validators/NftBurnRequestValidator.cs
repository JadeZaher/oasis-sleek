using FluentValidation;
using OASIS.WebAPI.Models.Requests;

namespace OASIS.WebAPI.Validators;

public class NftBurnRequestValidator : AbstractValidator<NftBurnRequest>
{
    public NftBurnRequestValidator()
    {
        RuleFor(x => x.WalletId)
            .NotEqual(Guid.Empty).WithMessage("WalletId must not be an empty GUID.");
    }
}
