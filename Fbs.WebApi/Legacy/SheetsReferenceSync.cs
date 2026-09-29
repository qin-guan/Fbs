using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Repository;
using Fbs.WebApi.Repository.Database;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using SqlSugar;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;
using DataUnit = Fbs.WebApi.Data.Entities.Unit;

namespace Fbs.WebApi.Legacy;

public sealed class SheetsSyncOptions
{
    /// <summary>Whether users, facilities and the roster are kept in step with the sheets. Off by default, which needs no Google.</summary>
    public bool Enabled { get; set; }

    /// <summary>How long after starting it first looks.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How often it looks.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The most of the members that may be taken off the Users sheet in one go. More is taken to be a mistake
    /// with the sheet, such as a sort that went wrong, and none of them are removed.
    /// </summary>
    public double MaxRemovedFraction { get; set; } = 0.5;
}

public sealed class SheetsReferenceSyncReport
{
    public ImportCounts Units { get; } = new();

    public ImportCounts Members { get; } = new();

    public ImportCounts Facilities { get; } = new();

    public ImportCounts Roster { get; } = new();

    public int MembersRemoved { get; set; }

    public int MembersRestored { get; set; }

    public int FacilitiesClosed { get; set; }

    public int RosterRemoved { get; set; }

    public List<string> Warnings { get; } = [];

    /// <summary>Whether anything was written.</summary>
    public bool Changed =>
        Units.Added + Members.Added + Members.Updated + Facilities.Added + Facilities.Updated + Roster.Added + Roster.Updated
        + MembersRemoved + MembersRestored + FacilitiesClosed + RosterRemoved > 0;
}

/// <summary>
/// Keeps a tenant's members, units, facilities and roster in step with the Users, Facilities and Nominal
/// Roll sheets, for as long as those are where they are edited, which is until there are screens to do it
/// in. It is what lets the sheets stay the place to add someone after bookings are in the database.
/// </summary>
/// <remarks>
/// <para>
/// The sheet decides who is a member, what they are called and which unit they are in, and every facility
/// and roster entry. The database keeps what is changed in the app: who is an admin, whom they hear about
/// on Telegram, and which chat is theirs. The sheet no longer says those, as it isn't written to.
/// </para>
/// <para>
/// Someone taken off the sheet is marked as having left, and can come back by being put back on it, and a
/// facility taken off can no longer be booked, but what they made and what was booked is kept. An empty
/// sheet, or taking most people off at once, is taken to be a mistake and removes nothing.
/// </para>
/// </remarks>
public sealed class SheetsReferenceSync(
    ISqlSugarClient sql,
    UserRepository users,
    FacilityRepository facilities,
    NominalRollRepository roster,
    DefaultTenant tenant,
    HybridCache cache,
    IOptions<SheetsSyncOptions> options
)
{
    public async Task<SheetsReferenceSyncReport> SyncAsync(CancellationToken cancellationToken = default)
    {
        var report = new SheetsReferenceSyncReport();

        // What the sheets say now, not what they said up to half a minute ago, which is how long reading them is
        // remembered for so that requests don't wait on Google
        await cache.RemoveAsync(["Facilities", "Nominal Roll", "Users"], cancellationToken);
        var legacyUsers = await users.GetListAsync(cancellationToken);
        var legacyFacilities = await facilities.GetListAsync(cancellationToken);
        var legacyRoster = await roster.GetListAsync(cancellationToken);
        var tenantId = await tenant.GetIdAsync(cancellationToken);

        using var tran = sql.Ado.UseTran();
        var units = await SyncUnitsAsync(tenantId, legacyUsers, legacyRoster, legacyFacilities, report, cancellationToken);
        await SyncMembersAsync(tenantId, legacyUsers, units, report, cancellationToken);
        await SyncFacilitiesAsync(tenantId, legacyFacilities, units, report, cancellationToken);
        await SyncRosterAsync(tenantId, legacyRoster, report, cancellationToken);
        tran.CommitTran();

        return report;
    }

    private async Task<Dictionary<string, Guid>> SyncUnitsAsync(
        Guid tenantId,
        List<Entities.User> legacyUsers,
        List<Entities.NominalRoll> legacyRoster,
        List<Entities.Facility> legacyFacilities,
        SheetsReferenceSyncReport report,
        CancellationToken cancellationToken
    )
    {
        var names = legacyUsers
            .Select(u => u.Unit)
            .Concat(legacyRoster.Select(r => r.Unit))
            .Concat(legacyFacilities.SelectMany(f => f.Scope ?? []).Where(scope => scope != "All"))
            .Select(name => name?.Trim())
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var units = (await sql.Queryable<DataUnit>().Where(u => u.TenantId == tenantId).ToListAsync(cancellationToken)).ToDictionary(u => u.Name, u => u.Id);
        var added = new List<DataUnit>();
        foreach (var name in names.Where(name => !units.ContainsKey(name)))
        {
            var unit = new DataUnit { Id = Guid.NewGuid(), TenantId = tenantId, Name = name };
            added.Add(unit);
            units[name] = unit.Id;
            report.Units.Added++;
        }

        if (added.Count > 0)
        {
            await sql.Insertable(added).ExecuteCommandAsync(cancellationToken);
        }

        return units;
    }

    private async Task SyncMembersAsync(
        Guid tenantId,
        List<Entities.User> legacyUsers,
        Dictionary<string, Guid> units,
        SheetsReferenceSyncReport report,
        CancellationToken cancellationToken
    )
    {
        var members = (await sql.Queryable<TenantMember>().Where(m => m.TenantId == tenantId && m.Phone != null).ToListAsync(cancellationToken)).ToDictionary(m => m.Phone!);
        var onSheet = new HashSet<string>();
        var added = new List<TenantMember>();
        var changed = new List<TenantMember>();

        foreach (var user in legacyUsers)
        {
            var phone = PhoneNumbers.ToStored(user.Phone);
            if (phone is null)
            {
                report.Warnings.Add($"Users row {user.Row} ({user.Name}) has no phone number, so it is not a member.");
                continue;
            }

            if (!onSheet.Add(phone))
            {
                report.Warnings.Add($"Users row {user.Row} ({user.Name}) has the same phone number as an earlier row, so it is not a member.");
                continue;
            }

            var unitId = user.Unit?.Trim() is { Length: > 0 } unit && units.TryGetValue(unit, out var id) ? id : (Guid?)null;
            var name = string.IsNullOrWhiteSpace(user.Name) ? phone : user.Name.Trim();

            if (!members.TryGetValue(phone, out var member))
            {
                member = new TenantMember
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Phone = phone,
                    DisplayName = name,
                    UnitId = unitId,
                    Role = user.IsAdmin ? MemberRole.Admin : MemberRole.Member,
                    NotificationScope = LegacyImporter.ScopeOf(user.NotificationGroup),
                    LegacyChatId = string.IsNullOrWhiteSpace(user.TelegramChatId) ? null : user.TelegramChatId.Trim(),
                    Status = MemberStatus.Unclaimed,
                };
                members[phone] = member;
                added.Add(member);
                report.Members.Added++;
                continue;
            }

            var restore = member.Status == MemberStatus.Removed;
            if (member.DisplayName == name && member.UnitId == unitId && !restore)
            {
                report.Members.Unchanged++;
                continue;
            }

            member.DisplayName = name;
            member.UnitId = unitId;
            if (restore)
            {
                // Who they were is kept, and they are members again
                member.Status = member.UserId is null ? MemberStatus.Unclaimed : MemberStatus.Active;
                report.MembersRestored++;
            }
            else
            {
                report.Members.Updated++;
            }

            changed.Add(member);
        }

        RemoveMembers(members, onSheet, changed, report);

        if (added.Count > 0)
        {
            await sql.Insertable(added).ExecuteCommandAsync(cancellationToken);
        }

        if (changed.Count > 0)
        {
            await sql.Updateable(changed).UpdateColumns(m => new { m.DisplayName, m.UnitId, m.Status }).ExecuteCommandAsync(cancellationToken);
        }
    }

    private void RemoveMembers(Dictionary<string, TenantMember> members, HashSet<string> onSheet, List<TenantMember> changed, SheetsReferenceSyncReport report)
    {
        var current = members.Values.Where(m => m.Status is MemberStatus.Unclaimed or MemberStatus.Active).ToList();
        var leaving = current.Where(m => !onSheet.Contains(m.Phone!)).ToList();
        if (leaving.Count == 0)
        {
            return;
        }

        if (onSheet.Count == 0)
        {
            report.Warnings.Add("The Users sheet has nobody on it, so nobody was removed.");
            return;
        }

        if (current.Count >= 5 && leaving.Count > current.Count * options.Value.MaxRemovedFraction)
        {
            report.Warnings.Add($"{leaving.Count} of {current.Count} members are not on the Users sheet, which is too many to be right, so nobody was removed.");
            return;
        }

        foreach (var member in leaving)
        {
            member.Status = MemberStatus.Removed;
            changed.Add(member);
            report.MembersRemoved++;
        }
    }

    private async Task SyncFacilitiesAsync(
        Guid tenantId,
        List<Entities.Facility> legacyFacilities,
        Dictionary<string, Guid> units,
        SheetsReferenceSyncReport report,
        CancellationToken cancellationToken
    )
    {
        var existing = (await sql.Queryable<DataFacility>().Where(f => f.TenantId == tenantId).ToListAsync(cancellationToken)).ToDictionary(f => f.Name);
        var access = (await sql.Queryable<FacilityUnitAccess>().Where(a => a.TenantId == tenantId).ToListAsync(cancellationToken))
            .GroupBy(a => a.FacilityId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.UnitId).ToHashSet());
        var onSheet = new HashSet<string>();
        var added = new List<DataFacility>();
        var newAccess = new List<FacilityUnitAccess>();

        foreach (var facility in legacyFacilities)
        {
            var name = facility.Name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                report.Warnings.Add($"Facilities row {facility.Row} has no name, so it is not a facility.");
                continue;
            }

            if (!onSheet.Add(name))
            {
                report.Warnings.Add($"Facilities row {facility.Row} ({name}) has the same name as an earlier row, so it is not a facility.");
                continue;
            }

            var scope = facility.Scope ?? [];
            var everyone = scope.Contains("All");
            var unitIds = everyone ? [] : scope.Select(s => s.Trim()).Where(units.ContainsKey).Select(s => units[s]).ToHashSet();
            var group = facility.Group?.Trim();

            if (!existing.TryGetValue(name, out var row))
            {
                row = new DataFacility { Id = Guid.NewGuid(), TenantId = tenantId, Name = name, Group = group, AvailableToAll = everyone };
                existing[name] = row;
                added.Add(row);
                var facilityId = row.Id;
                newAccess.AddRange(unitIds.Select(unitId => new FacilityUnitAccess { Id = Guid.NewGuid(), TenantId = tenantId, FacilityId = facilityId, UnitId = unitId }));
                report.Facilities.Added++;
                continue;
            }

            var current = access.GetValueOrDefault(row.Id) ?? [];
            if (row.Group == group && row.AvailableToAll == everyone && current.SetEquals(unitIds))
            {
                report.Facilities.Unchanged++;
                continue;
            }

            await ReplaceAsync(row, group, everyone, unitIds, newAccess, tenantId, cancellationToken);
            report.Facilities.Updated++;
        }

        if (onSheet.Count == 0 && existing.Count > 0)
        {
            report.Warnings.Add("The Facilities sheet has nothing on it, so no facility was closed.");
        }
        else
        {
            // Nobody can book what is not on the sheet, but what was booked stays
            foreach (var row in existing.Values.Where(f => !onSheet.Contains(f.Name) && !added.Contains(f)))
            {
                var current = access.GetValueOrDefault(row.Id) ?? [];
                if (!row.AvailableToAll && current.Count == 0)
                {
                    continue;
                }

                await ReplaceAsync(row, row.Group, everyone: false, [], newAccess, tenantId, cancellationToken);
                report.FacilitiesClosed++;
            }
        }

        if (added.Count > 0)
        {
            await sql.Insertable(added).ExecuteCommandAsync(cancellationToken);
        }

        if (newAccess.Count > 0)
        {
            await sql.Insertable(newAccess).ExecuteCommandAsync(cancellationToken);
        }
    }

    private async Task ReplaceAsync(
        DataFacility row,
        string? group,
        bool everyone,
        HashSet<Guid> unitIds,
        List<FacilityUnitAccess> newAccess,
        Guid tenantId,
        CancellationToken cancellationToken
    )
    {
        var id = row.Id;
        row.Group = group;
        row.AvailableToAll = everyone;
        await sql.Updateable(row).UpdateColumns(f => new { f.Group, f.AvailableToAll }).ExecuteCommandAsync(cancellationToken);
        await sql.Deleteable<FacilityUnitAccess>().Where(a => a.FacilityId == id).ExecuteCommandAsync(cancellationToken);
        newAccess.AddRange(unitIds.Select(unitId => new FacilityUnitAccess { Id = Guid.NewGuid(), TenantId = tenantId, FacilityId = id, UnitId = unitId }));
    }

    private async Task SyncRosterAsync(Guid tenantId, List<Entities.NominalRoll> legacyRoster, SheetsReferenceSyncReport report, CancellationToken cancellationToken)
    {
        var existing = (await sql.Queryable<RosterEntry>().Where(e => e.TenantId == tenantId).ToListAsync(cancellationToken)).ToDictionary(e => e.Phone);
        var onSheet = new HashSet<string>();
        var added = new List<RosterEntry>();
        var changed = new List<RosterEntry>();

        foreach (var entry in legacyRoster)
        {
            var phone = PhoneNumbers.ToStored(entry.Phone);
            if (phone is null)
            {
                report.Warnings.Add($"Nominal Roll row {entry.Row} ({entry.Name}) has no phone number, so it is not on the roster.");
                continue;
            }

            if (!onSheet.Add(phone))
            {
                report.Warnings.Add($"Nominal Roll row {entry.Row} ({entry.Name}) has the same phone number as an earlier row, so it is not on the roster.");
                continue;
            }

            var name = string.IsNullOrWhiteSpace(entry.Name) ? phone : entry.Name.Trim();
            var unit = string.IsNullOrWhiteSpace(entry.Unit) ? null : entry.Unit.Trim();
            if (!existing.TryGetValue(phone, out var row))
            {
                added.Add(new RosterEntry { Id = Guid.NewGuid(), TenantId = tenantId, Name = name, Unit = unit, Phone = phone });
                report.Roster.Added++;
            }
            else if (row.Name != name || row.Unit != unit)
            {
                row.Name = name;
                row.Unit = unit;
                changed.Add(row);
                report.Roster.Updated++;
            }
            else
            {
                report.Roster.Unchanged++;
            }
        }

        var gone = existing.Values.Where(e => !onSheet.Contains(e.Phone)).ToList();
        if (gone.Count > 0 && onSheet.Count == 0)
        {
            report.Warnings.Add("The Nominal Roll sheet has nobody on it, so nobody was taken off the roster.");
            gone = [];
        }

        if (added.Count > 0)
        {
            await sql.Insertable(added).ExecuteCommandAsync(cancellationToken);
        }

        if (changed.Count > 0)
        {
            await sql.Updateable(changed).UpdateColumns(e => new { e.Name, e.Unit }).ExecuteCommandAsync(cancellationToken);
        }

        if (gone.Count > 0)
        {
            var ids = gone.Select(e => e.Id).ToList();
            await sql.Deleteable<RosterEntry>().Where(e => ids.Contains(e.Id)).ExecuteCommandAsync(cancellationToken);
            report.RosterRemoved = gone.Count;
        }
    }
}
