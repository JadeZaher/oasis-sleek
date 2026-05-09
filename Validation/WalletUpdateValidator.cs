using FluentValidation;
using OASIS.WebAPI.Models.Requests;

namespace OASIS.WebAPI.Validation;

public class WalletUpdateValidator : AbstractValidator<WalletUpdateModel>
{
    public WalletUpdateValidator()
    {
        RuleFor(x => x.Label).MaximumLength(200).When(x => x.Label != null);
    }
}
