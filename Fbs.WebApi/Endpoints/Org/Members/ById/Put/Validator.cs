using FastEndpoints;
using FluentValidation;

namespace Fbs.WebApi.Endpoints.Org.Members.ById.Put;

public class Validator : MemberBodyValidator<Request>
{
    public Validator()
    {
        RuleFor(r => r.Membership).IsInEnum();
    }
}
