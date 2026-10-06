using FastEndpoints;
using FluentValidation;

namespace Fbs.WebApi.Endpoints.Org.Calendar.Confirm.Post;

public class Validator : Validator<Request>
{
    public Validator()
    {
        RuleFor(r => r.Code).Must(code => code.Trim().Length is >= 1 and <= 32).WithMessage("Type the code from the calendar.");
    }
}