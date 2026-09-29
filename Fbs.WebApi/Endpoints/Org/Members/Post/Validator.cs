using FastEndpoints;
using FluentValidation;

namespace Fbs.WebApi.Endpoints.Org.Members.Post;

public class Validator : MemberBodyValidator<Request>
{
    public Validator()
    {
        RuleFor(r => r.Phone).NotEmpty().WithMessage("A phone number is needed, as it is how they are found when they sign in.");
    }
}
