using FastEndpoints;
using FluentValidation;

namespace Fbs.WebApi.Endpoints.Booking.Batch.Post;

public class Validator : Validator<Request>
{
    public const int MaxSlots = 50;

    public Validator()
    {
        RuleFor(r => r.Conduct).NotEmpty().MaximumLength(100);

        RuleFor(r => r.Slots)
            .NotEmpty()
            .WithMessage("Select at least one time slot")
            .Must(s => s.Count <= MaxSlots)
            .WithMessage($"A batch can have at most {MaxSlots} bookings");

        RuleForEach(r => r.Slots)
            .ChildRules(slot =>
            {
                slot.RuleFor(s => s.FacilityName).NotEmpty();

                slot.RuleFor(s => s.StartDateTime)
                    .Must(s => s >= DateTimeOffset.Now)
                    .WithMessage("Start Date Time must be in the future");

                slot.RuleFor(s => s.EndDateTime)
                    .Must((s, end) => end > s.StartDateTime)
                    .WithMessage("End time must be after start time");

                slot.RuleFor(s => s.StartDateTime)
                    .Must(s => s.Minute % 30 == 0)
                    .WithMessage("Duration must be in 30 minute intervals");

                slot.RuleFor(s => s.EndDateTime)
                    .Must(s => s.Minute % 30 == 0)
                    .WithMessage("Duration must be in 30 minute intervals");
            });
    }
}
