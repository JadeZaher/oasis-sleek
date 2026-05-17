using FluentValidation;
using OASIS.WebAPI.Models.Requests;

namespace OASIS.WebAPI.Validators;

public class WalletTopUpRequestValidator : AbstractValidator<WalletTopUpRequest>
{
    public WalletTopUpRequestValidator()
    {
        // Null amount = fall back to the configured faucet default (server-side semantic).
        // A PRESENT amount must be strictly positive and within the test-network safety cap.
        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Amount must be greater than zero.")
            .LessThanOrEqualTo(1_000_000).WithMessage("Amount must not exceed 1,000,000 (test network safety limit).")
            .When(x => x.Amount.HasValue);
    }
}
