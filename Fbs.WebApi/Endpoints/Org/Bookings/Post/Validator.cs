using FastEndpoints;
using FluentValidation;

namespace Fbs.WebApi.Endpoints.Org.Bookings.Post;

public class Validator : Validator<Request>
{
    public const int MaxSlots = 50;

    public Validator()
    {
        RuleFor(r => r.Conduct).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Description).MaximumLength(2000);
        RuleFor(r => r.PocName).MaximumLength(200);
        RuleFor(r => r.PocPhone).MaximumLength(32);
        RuleFor(r => r.Slots).NotEmpty().WithMessage("Select at least one time slot").Must(s => s.Count <= MaxSlots).WithMessage($"At most {MaxSlots} can be booked together");
    }
}
