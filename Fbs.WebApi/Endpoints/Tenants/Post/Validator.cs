using FastEndpoints;
using FluentValidation;
using Fbs.WebApi.Tenancy;

namespace Fbs.WebApi.Endpoints.Tenants.Post;

public class Validator : Validator<Request>
{
    public Validator()
    {
        RuleFor(r => r.Name).Must(name => name.Trim().Length is >= 2 and <= 100).WithMessage("The name has to be between 2 and 100 characters.");
        RuleFor(r => r.Slug)
            .Must(Slugs.IsValid)
            .WithMessage("The address can only have lower case letters, digits and hyphens, from 3 to 63 characters, and starts and ends with a letter or digit.");
        RuleFor(r => r.TimeZone).Must(TimeZones.IsKnown).When(r => !string.IsNullOrWhiteSpace(r.TimeZone)).WithMessage("That is not a time zone this server knows.");
        RuleFor(r => r.DefaultCountryCode)
            .Matches("^[0-9]{1,4}$")
            .When(r => !string.IsNullOrWhiteSpace(r.DefaultCountryCode))
            .WithMessage("The calling code is 1 to 4 digits, without a plus.");
    }
}
