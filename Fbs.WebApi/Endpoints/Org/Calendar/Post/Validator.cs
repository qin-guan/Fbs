using FastEndpoints;
using FluentValidation;

namespace Fbs.WebApi.Endpoints.Org.Calendar.Post;

public class Validator : Validator<Request>
{
    public Validator()
    {
        RuleFor(r => r.CalendarId)
            .Must(id =>
            {
                var trimmed = id.Trim();
                return trimmed.Length is >= 1 and <= 256
                    && trimmed.All(c => !char.IsWhiteSpace(c) && c is not '/' and not '?' and not '#')
                    && !trimmed.Equals("primary", StringComparison.OrdinalIgnoreCase);
            })
            .WithMessage("The calendar id is the address of the calendar, up to 256 characters, and not primary.");
    }
}