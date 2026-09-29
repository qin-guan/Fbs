using FluentValidation;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tenancy;
using SqlSugar;

namespace Fbs.WebApi.Endpoints.Org.Members;

/// <summary>What is sent to add or change a member.</summary>
public class MemberBody
{
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>With a plus, or 00, it is taken as it is, and without it is in the organisation's country.</summary>
    public string? Phone { get; set; }

    public Guid? UnitId { get; set; }

    public MemberRole Role { get; set; } = MemberRole.Member;

    public NotificationScope NotificationScope { get; set; } = NotificationScope.None;
}

public class MemberBodyValidator<T> : FastEndpoints.Validator<T>
    where T : MemberBody
{
    public MemberBodyValidator()
    {
        RuleFor(r => r.DisplayName).Must(name => name.Trim().Length is >= 1 and <= 200).WithMessage("The name has to be between 1 and 200 characters.");
        RuleFor(r => r.Role).IsInEnum();
        RuleFor(r => r.NotificationScope).IsInEnum();
    }
}

public static class MemberChecks
{
    /// <summary>Whether the unit is one of the organisation's, or there is none.</summary>
    public static async Task<bool> UnitIsInAsync(ISqlSugarClient sql, Guid tenantId, Guid? unitId, CancellationToken ct)
    {
        if (unitId is null)
        {
            return true;
        }

        var id = unitId.Value;
        return await sql.Queryable<Data.Entities.Unit>().AnyAsync(u => u.Id == id && u.TenantId == tenantId, ct);
    }

    /// <summary>How many active admins there are, read so that no other change can be made to them until the transaction ends.</summary>
    public static async Task<List<TenantMember>> LockActiveAdminsAsync(ISqlSugarClient sql, Guid tenantId, CancellationToken ct)
    {
        var admin = MemberRole.Admin;
        var active = MemberStatus.Active;
        return await sql.Queryable<TenantMember>().Where(m => m.TenantId == tenantId && m.Role == admin && m.Status == active).TranLock(DbLockType.Wait).ToListAsync(ct);
    }
}
