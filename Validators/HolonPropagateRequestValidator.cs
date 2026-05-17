using FluentValidation;
using OASIS.WebAPI.Models.Requests;

namespace OASIS.WebAPI.Validators;

public class HolonPropagateRequestValidator : AbstractValidator<HolonPropagateRequest>
{
    private static readonly string[] AllowedProperties = { "IsActive" };

    public HolonPropagateRequestValidator()
    {
        RuleFor(x => x.Property)
            .NotEmpty().WithMessage("Property is required.")
            .Must(p => AllowedProperties.Contains(p))
            .WithMessage($"Property must be one of: {string.Join(", ", AllowedProperties)}.");
    }
}
