using FluentValidation;
using OASIS.WebAPI.Models.Requests;

namespace OASIS.WebAPI.Validation;

public class WalletCreateValidator : AbstractValidator<WalletCreateModel>
{
    public WalletCreateValidator()
    {
        RuleFor(x => x.ChainType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Address).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Label).MaximumLength(200);
    }
}
