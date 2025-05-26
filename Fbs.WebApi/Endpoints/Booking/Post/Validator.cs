using FastEndpoints;
using FluentValidation;

namespace Fbs.WebApi.Endpoints.Booking.Post;

public class Validator: Validator<Request>
{
    public Validator()
    {
        RuleFor(r => r.StartDateTime)
            .Must(s => s >= DateTimeOffset.Now)
            .WithMessage("Start Date Time must be in the future");

        RuleFor(r => r.EndDateTime)
            .Must((r, _) => r.EndDateTime > r.StartDateTime)
            .WithMessage("End time must be after start time");

        RuleFor(r => r.StartDateTime)
            .Must(s => s.Minute % 30 == 0)
            .WithMessage("Duration must be in 30 minute intervals");
        
        RuleFor(r => r.EndDateTime)
            .Must(s => s.Minute % 30 == 0)
            .WithMessage("Duration must be in 30 minute intervals");

        RuleFor(r => r.Conduct)
            .NotEmpty()
            .MaximumLength(100);
    }
}