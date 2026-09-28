using FastEndpoints;
using FluentValidation;

namespace Fbs.WebApi.Endpoints.Booking.ById.Post;

public class Validator : Validator<Request>
{
    public Validator()
    {
        RuleFor(r => r.Conduct).NotEmpty().MaximumLength(100);

        RuleFor(r => r.EndDateTime)
            .NotNull()
            .When(r => r.StartDateTime is not null)
            .WithMessage("End time is required when changing the time slot");

        RuleFor(r => r.StartDateTime)
            .NotNull()
            .When(r => r.EndDateTime is not null)
            .WithMessage("Start time is required when changing the time slot");

        When(
            r => r.StartDateTime is not null && r.EndDateTime is not null,
            () =>
            {
                RuleFor(r => r.EndDateTime)
                    .Must((r, end) => end > r.StartDateTime)
                    .WithMessage("End time must be after start time");

                RuleFor(r => r.StartDateTime)
                    .Must(s => s!.Value.Minute % 30 == 0)
                    .WithMessage("Duration must be in 30 minute intervals");

                RuleFor(r => r.EndDateTime)
                    .Must(s => s!.Value.Minute % 30 == 0)
                    .WithMessage("Duration must be in 30 minute intervals");
            }
        );
    }
}
