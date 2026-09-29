using FastEndpoints;
using FluentValidation;
using Fbs.WebApi.Auth.Clerk;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using SqlSugar;

namespace Fbs.WebApi.Endpoints.Org.Settings.Put;

public class Request
{
    public string Name { get; set; } = string.Empty;

    public string TimeZone { get; set; } = string.Empty;

    public string DefaultCountryCode { get; set; } = string.Empty;

    /// <summary>15, 30 or 60: the length of the smallest slot bookings start and end on.</summary>
    public int SlotMinutes { get; set; }

    public bool RequireApproval { get; set; }
}

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

[RequiresClerk]
public class Endpoint(ITenantContext tenantContext, ISqlSugarClient sql) : Endpoint<Request, Get.Response>
{
    public override void Configure()
    {
        Put("Settings");
        Group<TenantAdminGroup>();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var id = tenantContext.Tenant.Id;
        var name = req.Name.Trim();
        var timeZone = req.TimeZone.Trim();
        var countryCode = req.DefaultCountryCode.Trim();
        await sql.Updateable<Tenant>()
            .SetColumns(t => new Tenant
            {
                Name = name,
                TimeZone = timeZone,
                DefaultCountryCode = countryCode,
                SlotMinutes = req.SlotMinutes,
                RequireApproval = req.RequireApproval,
            })
            .Where(t => t.Id == id)
            .ExecuteCommandAsync(ct);

        await Send.OkAsync(
            new Get.Response
            {
                Name = name,
                TimeZone = timeZone,
                DefaultCountryCode = countryCode,
                SlotMinutes = req.SlotMinutes,
                RequireApproval = req.RequireApproval,
            },
            ct
        );
    }
}
