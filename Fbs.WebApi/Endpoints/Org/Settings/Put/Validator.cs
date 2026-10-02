using FastEndpoints;
using FluentValidation;
using Fbs.WebApi.Tenancy;

namespace Fbs.WebApi.Endpoints.Org.Settings.Put;

public class Validator : Validator<Request>
{
    public Validator()
    {
        RuleFor(r => r.Name).Must(name => name.Trim().Length is >= 2 and <= 100).WithMessage("The name has to be between 2 and 100 characters.");
        RuleFor(r => r.TimeZone).Must(TimeZones.IsKnown).WithMessage("That is not a time zone this server knows.");
        RuleFor(r => r.DefaultCountryCode).Matches("^[0-9]{1,4}$").WithMessage("The calling code is 1 to 4 digits, without a plus.");
        RuleFor(r => r.SlotMinutes).Must(minutes => minutes is 15 or 30 or 60).WithMessage("A slot is 15, 30 or 60 minutes.");
    }
}
