using FastEndpoints;
using FluentValidation;

namespace Fbs.WebApi.Endpoints.Invites.ByToken.Accept.Post;

public class Validator : Validator<Request>
{
    public Validator()
    {
        RuleFor(r => r.DisplayName).MaximumLength(200);
    }
}
