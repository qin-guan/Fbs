using FastEndpoints;
using FluentValidation;
using Fbs.WebApi.Tenancy;

namespace Fbs.WebApi.Endpoints.Org.Me.Put;

public class Validator : Validator<Request>
{
    public Validator()
    {
        RuleFor(r => r.DisplayName).Must(name => name.Trim().Length is >= 1 and <= MemberNames.MaxLength).WithMessage("The name has to be between 1 and 200 characters.");
    }
}
