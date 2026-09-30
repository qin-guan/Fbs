using Fbs.WebApi.Data.Entities;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace Fbs.WebApi.Tenancy;

public enum DeletionOutcome
{
    /// <summary>It is to be deleted now, or it was, and is restored now.</summary>
    Changed = 1,

    /// <summary>There is no such organisation, or they are not an admin of it, which are told the same way.</summary>
    NotFound = 2,

    /// <summary>Asked to delete what is to be deleted, or to restore what isn't, or what is not theirs to: nothing was done.</summary>
    NotInThatState = 3,
}

/// <summary>
/// An admin deleting their organisation, which is done in two steps so that a mistake, or a change of mind, can be undone: it
/// is asked for, and nobody can use it from then, and it is deleted for good some days later, by <see cref="TenantPurges"/>. Until
/// then any admin of it can restore it, from where they are told it is to be deleted, as everything else of it is refused.
/// </summary>
public sealed class TenantDeletions(ISqlSugarClient sql, IOptions<TenantLimits> options)
{
    /// <returns>When it is deleted for good, if it is now to be.</returns>
    public async Task<(DeletionOutcome Outcome, DateTimeOffset? DeleteAfter)> RequestAsync(string slug, Guid accountId, CancellationToken ct)
    {
        var deleteAfter = DateTimeOffset.UtcNow.AddDays(options.Value.DeletionGraceDays);
        using var tran = sql.Ado.UseTran();
        var found = await FindAsync(slug, accountId, ct);
        if (found is null)
        {
            return (DeletionOutcome.NotFound, null);
        }

        var (tenant, admin) = found.Value;
        if (tenant.Status != TenantStatus.Active)
        {
            return (DeletionOutcome.NotInThatState, null);
        }

        var id = tenant.Id;
        await sql.Updateable<Tenant>().SetColumns(t => new Tenant { Status = TenantStatus.PendingDeletion, DeleteAfter = deleteAfter }).Where(t => t.Id == id).ExecuteCommandAsync(ct);
        await new AuditLog(sql).WriteAsync(id, admin.Id, "tenant.deletion_requested", $"Asked for the organisation to be deleted, on {deleteAfter:d MMMM yyyy} at the earliest.", "tenant", id, ct);
        tran.CommitTran();
        return (DeletionOutcome.Changed, deleteAfter);
    }

    public async Task<DeletionOutcome> RestoreAsync(string slug, Guid accountId, CancellationToken ct)
    {
        using var tran = sql.Ado.UseTran();
        var found = await FindAsync(slug, accountId, ct);
        if (found is null)
        {
            return DeletionOutcome.NotFound;
        }

        var (tenant, admin) = found.Value;
        if (tenant.Status != TenantStatus.PendingDeletion)
        {
            return DeletionOutcome.NotInThatState;
        }

        var id = tenant.Id;
        await sql.Updateable<Tenant>().SetColumns(t => new Tenant { Status = TenantStatus.Active, DeleteAfter = null }).Where(t => t.Id == id).ExecuteCommandAsync(ct);
        await new AuditLog(sql).WriteAsync(id, admin.Id, "tenant.restored", "Restored the organisation, which was to be deleted.", "tenant", id, ct);
        tran.CommitTran();
        return DeletionOutcome.Changed;
    }

    /// <summary>The organisation, locked, and the admin of it that the account is: nobody else can ask for this.</summary>
    private async Task<(Tenant Tenant, TenantMember Admin)?> FindAsync(string slug, Guid accountId, CancellationToken ct)
    {
        var tenant = await sql.Queryable<Tenant>().Where(t => t.Slug == slug).TranLock(DbLockType.Wait).FirstAsync(ct);
        if (tenant is null)
        {
            return null;
        }

        var tenantId = tenant.Id;
        var admin = MemberRole.Admin;
        var active = MemberStatus.Active;
        var member = await sql.Queryable<TenantMember>().FirstAsync(m => m.TenantId == tenantId && m.UserId == accountId && m.Role == admin && m.Status == active, ct);
        return member is null ? null : (tenant, member);
    }
}
