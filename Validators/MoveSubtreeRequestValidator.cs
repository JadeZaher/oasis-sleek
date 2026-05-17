using FluentValidation;
using OASIS.WebAPI.Models.Requests;

namespace OASIS.WebAPI.Validators;

public class MoveSubtreeRequestValidator : AbstractValidator<MoveSubtreeRequest>
{
    public MoveSubtreeRequestValidator()
    {
        RuleFor(x => x.NewParentId)
            .NotEqual(Guid.Empty).WithMessage("NewParentId must not be an empty GUID.");
    }
}
