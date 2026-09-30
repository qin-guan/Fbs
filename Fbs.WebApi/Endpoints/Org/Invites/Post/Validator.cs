using FastEndpoints;
using FluentValidation;

namespace Fbs.WebApi.Endpoints.Org.Invites.Post;

public class Validator : Validator<Request>
{
    public Validator()
    {
        RuleFor(r => r.Role).IsInEnum();
        RuleFor(r => r.ExpiresInDays).InclusiveBetween(1, 30);
        RuleFor(r => r.MaxUses).InclusiveBetween(1, 100);
    }
}
