using FluentValidation;
using OASIS.WebAPI.Models;

namespace OASIS.WebAPI.Validation;

public class AvatarLoginValidator : AbstractValidator<AvatarLoginModel>
{
    public AvatarLoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}
