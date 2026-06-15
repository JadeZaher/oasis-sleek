using FluentValidation;
using OASIS.WebAPI.Models;

namespace OASIS.WebAPI.Validators;

public class AvatarLoginValidator : AbstractValidator<AvatarLoginModel>
{
    public AvatarLoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}
