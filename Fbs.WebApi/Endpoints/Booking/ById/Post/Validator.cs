using FastEndpoints;
using FluentValidation;

namespace Fbs.WebApi.Endpoints.Booking.ById.Post;

public class Validator: Validator<Request>
{
    public Validator()
    {
        RuleFor(r => r.Conduct)
            .NotEmpty()
            .MaximumLength(100);
    }
}