using FastEndpoints;
using FluentValidation;

namespace Fbs.WebApi.Endpoints.Tenants.Deletion.Post;

public class Validator : Validator<Request>
{
    public Validator()
    {
        RuleFor(r => r.Confirm).Must((r, confirm) => string.Equals(confirm?.Trim(), r.Slug, StringComparison.Ordinal)).WithMessage("Type the address of the organisation to say that you mean it.");
    }
}
