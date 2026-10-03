using FastEndpoints;
using FluentValidation;

namespace Fbs.WebApi.Endpoints.Org.Bookings.ById.Put;

public class Validator : Validator<Request>
{
    public Validator()
    {
        RuleFor(r => r.Conduct).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Description).MaximumLength(2000);
        RuleFor(r => r.PocName).MaximumLength(200);
        RuleFor(r => r.PocPhone).MaximumLength(32);
        RuleFor(r => r.EndDateTime).NotNull().When(r => r.StartDateTime is not null).WithMessage("The end is needed when changing the time.");
        RuleFor(r => r.StartDateTime).NotNull().When(r => r.EndDateTime is not null).WithMessage("The start is needed when changing the time.");
    }
}
