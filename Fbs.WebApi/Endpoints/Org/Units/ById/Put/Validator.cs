using FastEndpoints;
using FluentValidation;

namespace Fbs.WebApi.Endpoints.Org.Units.ById.Put;

public class Validator : Validator<Request>
{
    public Validator()
    {
        RuleFor(r => r.Name).Must(name => name.Trim().Length is >= 1 and <= 100).WithMessage("The name has to be between 1 and 100 characters.");
    }
}
