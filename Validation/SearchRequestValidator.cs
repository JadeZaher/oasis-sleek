using FluentValidation;
using OASIS.WebAPI.Models.Requests;

namespace OASIS.WebAPI.Validation;

public class SearchRequestValidator : AbstractValidator<SearchRequest>
{
    private static readonly string[] ValidSortFields = { "CreatedDate", "Name", "Relevance" };

    public SearchRequestValidator()
    {
        RuleFor(x => x.Query).MaximumLength(500);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.SortBy).Must(x => ValidSortFields.Contains(x))
            .WithMessage($"SortBy must be one of: {string.Join(", ", ValidSortFields)}");
    }
}
