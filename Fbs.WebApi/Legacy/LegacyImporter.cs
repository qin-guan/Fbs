using System.Text.RegularExpressions;
using Fbs.WebApi.Bookings;
using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Repository;
using SqlSugar;
using Booking = Fbs.WebApi.Entities.Booking;
using DataBooking = Fbs.WebApi.Data.Entities.Booking;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;
using DataUnit = Fbs.WebApi.Data.Entities.Unit;

namespace Fbs.WebApi.Legacy;

public sealed class LegacyImportOptions
{
    /// <summary>The tenant the data goes to, which is made if it isn't there.</summary>
    public required string Slug { get; init; }

    /// <summary>The name the tenant is made with. Defaults to the slug.</summary>
    public string? Name { get; init; }

    /// <summary>Does everything, including checking that it would work, and then undoes it.</summary>
    public bool DryRun { get; init; }

    /// <summary>
    /// Replaces what was imported before with what is there now, for the last import before switching over.
    /// Without it, what is already in the database is left as it is, and only what is new is added.
    /// </summary>
    public bool Overwrite { get; init; }
}

public sealed class ImportCounts
{
    public int Added { get; set; }

    public int Updated { get; set; }

    public int Unchanged { get; set; }

    public override string ToString() => $"{Added} added, {Updated} updated, {Unchanged} unchanged";
}

public sealed class LegacyImportReport
{
    public bool DryRun { get; init; }

    public bool TenantCreated { get; set; }

    public ImportCounts Units { get; } = new();

    public ImportCounts Members { get; } = new();

    public ImportCounts Facilities { get; } = new();

    public ImportCounts Roster { get; } = new();

    public ImportCounts Bookings { get; } = new();

    /// <summary>Things about the data that are worth knowing, none of which stopped it being imported.</summary>
    public List<string> Warnings { get; } = [];

    public IEnumerable<string> Summary()
    {
        yield return DryRun ? "Dry run: nothing was saved." : "Imported.";
        yield return $"Tenant: {(TenantCreated ? "created" : "already there")}";
        yield return $"Units: {Units}";
        yield return $"Members: {Members}";
        yield return $"Facilities: {Facilities}";
        yield return $"Roster: {Roster}";
        yield return $"Bookings: {Bookings}";
        foreach (var warning in Warnings)
        {
            yield return $"Warning: {warning}";
        }
    }
}

/// <summary>
/// Moves a tenant's users, facilities, roster and bookings from Google Sheets and Calendar into the
/// database, keeping the IDs bookings already have, so they, and the events that go with them, stay the same.
/// </summary>
/// <remarks>
/// <para>
/// It can be run as often as needed before switching over. It only adds what is new unless it is told to
/// <see cref="LegacyImportOptions.Overwrite"/>, which it refuses to do once anything has been done in
/// the database, as that would replace it with what is in Google, which is by then out of date.
/// </para>
/// <para>
/// Everything is done in one transaction, so it is all imported or none of it, and a dry run is the same
/// thing with the transaction not kept.
/// </para>
/// <para>
/// Legacy bookings are read through <see cref="IBookingService"/> from the calendar, which decodes what the
/// old version stored in its events. That is why <see cref="Booking"/> has to keep the shape it has until
/// this has been used for the last time.
/// </para>
/// </remarks>
public sealed partial class LegacyImporter(
    ISqlSugarClient sql,
    IUserRepository users,
    IFacilityRepository facilities,
    INominalRollRepository roster,
    IBookingService legacyBookings
)
{
    /// <summary>How many rows are written at a time, as one statement with thousands of them is refused.</summary>
    private const int Chunk = 500;

    private const int ConductLength = 200;
    private const int DescriptionLength = 2000;
    private const int PocNameLength = 200;
    private const int PocPhoneLength = 32;

    /// <summary>The group of a facility that is no longer on the sheet, but still has bookings.</summary>
    private const string RemovedFacilityGroup = "Removed";

    /// <summary>The member that bookings with no phone number are by.</summary>
    private const string NobodyName = "Unknown";

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$")]
    private static partial Regex SlugPattern();

    public async Task<LegacyImportReport> ImportAsync(LegacyImportOptions options, CancellationToken cancellationToken = default)
    {
        if (!SlugPattern().IsMatch(options.Slug))
        {
            throw new ArgumentException("A slug is lower case letters, digits and hyphens, and starts and ends with a letter or digit.", nameof(options));
        }

        var report = new LegacyImportReport { DryRun = options.DryRun };
        var legacyUsers = await users.GetListAsync(cancellationToken);
        var legacyFacilities = await facilities.GetListAsync(cancellationToken);
        var legacyRoster = await roster.GetListAsync(cancellationToken);
        var bookings = await legacyBookings.ListAsync(cancellationToken);

        // Disposing without committing undoes it all, which is what a dry run is
        using var tran = sql.Ado.UseTran();

        var tenant = await GetOrCreateTenantAsync(options, report, cancellationToken);
        if (options.Overwrite && !report.TenantCreated)
        {
            await RefuseIfUsedAsync(tenant, cancellationToken);
        }

        var tenantId = tenant.Id;
        var units = await ImportUnitsAsync(tenantId, legacyUsers, legacyRoster, legacyFacilities, report, cancellationToken);
        var members = await ImportMembersAsync(tenantId, legacyUsers, units, options, report, cancellationToken);
        var facilityIds = await ImportFacilitiesAsync(tenantId, legacyFacilities, units, options, report, cancellationToken);
        await ImportRosterAsync(tenantId, legacyRoster, options, report, cancellationToken);
        await ImportBookingsAsync(tenantId, bookings, members, facilityIds, options, report, cancellationToken);

        if (!options.DryRun)
        {
            tran.CommitTran();
        }

        return report;
    }

    private async Task<Tenant> GetOrCreateTenantAsync(LegacyImportOptions options, LegacyImportReport report, CancellationToken cancellationToken)
    {
        var slug = options.Slug;
        var tenant = await sql.Queryable<Tenant>().FirstAsync(t => t.Slug == slug, cancellationToken);
        if (tenant is not null)
        {
            return tenant;
        }

        tenant = new Tenant { Id = Guid.NewGuid(), Slug = slug, Name = options.Name ?? slug };
        await sql.Insertable(tenant).ExecuteCommandAsync(cancellationToken);
        report.TenantCreated = true;
        return tenant;
    }

    private async Task RefuseIfUsedAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        var tenantId = tenant.Id;
        if (await sql.Queryable<OutboxMessage>().AnyAsync(m => m.TenantId == tenantId, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Tenant '{tenant.Slug}' has had bookings made, changed or cancelled in the database, and importing over it would replace them with what is in Google, which is out of date by now."
            );
        }
    }

    private async Task<Dictionary<string, Guid>> ImportUnitsAsync(
        Guid tenantId,
        List<Entities.User> legacyUsers,
        List<Entities.NominalRoll> legacyRoster,
        List<Entities.Facility> legacyFacilities,
        LegacyImportReport report,
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
            .Order(StringComparer.Ordinal)
            .ToList();

        var units = (await sql.Queryable<DataUnit>().Where(u => u.TenantId == tenantId).ToListAsync(cancellationToken)).ToDictionary(u => u.Name, u => u.Id);
        var added = new List<DataUnit>();
        foreach (var name in names)
        {
            if (units.ContainsKey(name))
            {
                report.Units.Unchanged++;
                continue;
            }

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

    /// <returns>Every member of the tenant, by phone number, including those made for people who only appear on bookings.</returns>
    private async Task<Dictionary<string, TenantMember>> ImportMembersAsync(
        Guid tenantId,
        List<Entities.User> legacyUsers,
        Dictionary<string, Guid> units,
        LegacyImportOptions options,
        LegacyImportReport report,
        CancellationToken cancellationToken
    )
    {
        var members = (await sql.Queryable<TenantMember>().Where(m => m.TenantId == tenantId && m.Phone != null).ToListAsync(cancellationToken))
            .ToDictionary(m => m.Phone!);
        // The one for bookings that have no phone number, from an earlier import
        var nobody = await sql.Queryable<TenantMember>().FirstAsync(m => m.TenantId == tenantId && m.Phone == null && m.DisplayName == NobodyName && m.Status == MemberStatus.Removed, cancellationToken);
        if (nobody is not null)
        {
            members[string.Empty] = nobody;
        }

        var seen = new HashSet<string>();
        var added = new List<TenantMember>();
        var changed = new List<TenantMember>();

        foreach (var user in legacyUsers)
        {
            var phone = PhoneNumbers.ToStored(user.Phone);
            if (phone is null)
            {
                report.Warnings.Add($"Users row {user.Row} ({user.Name}) has no phone number, so it was not imported.");
                continue;
            }

            if (!seen.Add(phone))
            {
                report.Warnings.Add($"Users row {user.Row} ({user.Name}) has the same phone number as an earlier row, so it was not imported.");
                continue;
            }

            var unitId = user.Unit?.Trim() is { Length: > 0 } unit && units.TryGetValue(unit, out var id) ? id : (Guid?)null;
            var scope = ScopeOf(user.NotificationGroup);
            var role = user.IsAdmin ? MemberRole.Admin : MemberRole.Member;
            var chat = string.IsNullOrWhiteSpace(user.TelegramChatId) ? null : user.TelegramChatId.Trim();
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
                    Role = role,
                    NotificationScope = scope,
                    LegacyChatId = chat,
                    // Nobody has signed in with an account yet, so nobody has claimed their place
                    Status = MemberStatus.Unclaimed,
                };
                members[phone] = member;
                added.Add(member);
                report.Members.Added++;
                continue;
            }

            // Made for someone who only appeared on bookings, and now turns up on the sheet
            var placeholder = member is { Status: MemberStatus.Removed, UserId: null };
            var replace = placeholder || (options.Overwrite && member.UserId is null);
            var differs = member.DisplayName != name || member.UnitId != unitId || member.Role != role || member.NotificationScope != scope || member.LegacyChatId != chat;
            if (replace && (differs || placeholder))
            {
                member.DisplayName = name;
                member.UnitId = unitId;
                member.Role = role;
                member.NotificationScope = scope;
                member.LegacyChatId = chat;
                if (placeholder)
                {
                    member.Status = MemberStatus.Unclaimed;
                }

                changed.Add(member);
                report.Members.Updated++;
            }
            else
            {
                report.Members.Unchanged++;
            }
        }

        if (added.Count > 0)
        {
            await InsertAsync(added, cancellationToken);
        }

        if (changed.Count > 0)
        {
            await sql.Updateable(changed).UpdateColumns(m => new { m.DisplayName, m.UnitId, m.Role, m.NotificationScope, m.LegacyChatId, m.Status }).ExecuteCommandAsync(cancellationToken);
        }

        return members;
    }

    private async Task<Dictionary<string, Guid>> ImportFacilitiesAsync(
        Guid tenantId,
        List<Entities.Facility> legacyFacilities,
        Dictionary<string, Guid> units,
        LegacyImportOptions options,
        LegacyImportReport report,
        CancellationToken cancellationToken
    )
    {
        var existing = (await sql.Queryable<DataFacility>().Where(f => f.TenantId == tenantId).ToListAsync(cancellationToken)).ToDictionary(f => f.Name);
        var accessRows = await sql.Queryable<FacilityUnitAccess>().Where(a => a.TenantId == tenantId).ToListAsync(cancellationToken);
        var seen = new HashSet<string>();
        var added = new List<DataFacility>();
        var access = new List<FacilityUnitAccess>();

        foreach (var facility in legacyFacilities)
        {
            var name = facility.Name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                report.Warnings.Add($"Facilities row {facility.Row} has no name, so it was not imported.");
                continue;
            }

            if (!seen.Add(name))
            {
                report.Warnings.Add($"Facilities row {facility.Row} ({name}) has the same name as an earlier row, so it was not imported.");
                continue;
            }

            var scope = facility.Scope ?? [];
            var everyone = scope.Contains("All");
            var unitIds = everyone ? [] : scope.Select(s => s.Trim()).Where(units.ContainsKey).Select(s => units[s]).Distinct().ToList();

            if (!existing.TryGetValue(name, out var row))
            {
                row = new DataFacility { Id = Guid.NewGuid(), TenantId = tenantId, Name = name, Group = facility.Group?.Trim(), AvailableToAll = everyone };
                existing[name] = row;
                added.Add(row);
                var facilityId = row.Id;
                access.AddRange(unitIds.Select(unitId => new FacilityUnitAccess { Id = Guid.NewGuid(), TenantId = tenantId, FacilityId = facilityId, UnitId = unitId }));
                report.Facilities.Added++;
                continue;
            }

            var group = facility.Group?.Trim();
            var id = row.Id;
            var current = accessRows.Where(a => a.FacilityId == id).Select(a => a.UnitId).ToHashSet();
            var differs = row.Group != group || row.AvailableToAll != everyone || !current.SetEquals(unitIds);
            if (!options.Overwrite || !differs)
            {
                report.Facilities.Unchanged++;
                continue;
            }

            row.Group = group;
            row.AvailableToAll = everyone;
            await sql.Updateable(row).UpdateColumns(f => new { f.Group, f.AvailableToAll }).ExecuteCommandAsync(cancellationToken);
            await sql.Deleteable<FacilityUnitAccess>().Where(a => a.FacilityId == id).ExecuteCommandAsync(cancellationToken);
            access.AddRange(unitIds.Select(unitId => new FacilityUnitAccess { Id = Guid.NewGuid(), TenantId = tenantId, FacilityId = id, UnitId = unitId }));
            report.Facilities.Updated++;
        }

        if (added.Count > 0)
        {
            await sql.Insertable(added).ExecuteCommandAsync(cancellationToken);
        }

        if (access.Count > 0)
        {
            await InsertAsync(access, cancellationToken);
        }

        return existing.ToDictionary(f => f.Key, f => f.Value.Id);
    }

    private async Task ImportRosterAsync(
        Guid tenantId,
        List<Entities.NominalRoll> legacyRoster,
        LegacyImportOptions options,
        LegacyImportReport report,
        CancellationToken cancellationToken
    )
    {
        var existing = (await sql.Queryable<RosterEntry>().Where(e => e.TenantId == tenantId).ToListAsync(cancellationToken)).ToDictionary(e => e.Phone);
        var seen = new HashSet<string>();
        var added = new List<RosterEntry>();
        var changed = new List<RosterEntry>();

        foreach (var entry in legacyRoster)
        {
            var phone = PhoneNumbers.ToStored(entry.Phone);
            if (phone is null)
            {
                report.Warnings.Add($"Nominal Roll row {entry.Row} ({entry.Name}) has no phone number, so it was not imported.");
                continue;
            }

            if (!seen.Add(phone))
            {
                report.Warnings.Add($"Nominal Roll row {entry.Row} ({entry.Name}) has the same phone number as an earlier row, so it was not imported.");
                continue;
            }

            var name = string.IsNullOrWhiteSpace(entry.Name) ? phone : entry.Name.Trim();
            var unit = string.IsNullOrWhiteSpace(entry.Unit) ? null : entry.Unit.Trim();
            if (!existing.TryGetValue(phone, out var row))
            {
                added.Add(new RosterEntry { Id = Guid.NewGuid(), TenantId = tenantId, Name = name, Unit = unit, Phone = phone });
                report.Roster.Added++;
            }
            else if (options.Overwrite && (row.Name != name || row.Unit != unit))
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

        if (added.Count > 0)
        {
            await InsertAsync(added, cancellationToken);
        }

        if (changed.Count > 0)
        {
            await sql.Updateable(changed).UpdateColumns(e => new { e.Name, e.Unit }).ExecuteCommandAsync(cancellationToken);
        }
    }

    private async Task ImportBookingsAsync(
        Guid tenantId,
        List<Booking> legacy,
        Dictionary<string, TenantMember> members,
        Dictionary<string, Guid> facilityIds,
        LegacyImportOptions options,
        LegacyImportReport report,
        CancellationToken cancellationToken
    )
    {
        var existing = (await sql.Queryable<DataBooking>().Where(b => b.TenantId == tenantId).ToListAsync(cancellationToken)).ToDictionary(b => b.Id);
        var now = DateTimeOffset.UtcNow;
        var added = new List<DataBooking>();
        var changed = new List<DataBooking>();
        var valid = new List<Booking>();
        var newFacilities = new List<DataFacility>();
        var newMembers = new List<TenantMember>();
        var truncated = 0;
        var orphaned = new HashSet<string>();
        var unknownFacilities = new HashSet<string>();

        string? Truncate(string? text, int length)
        {
            if (text is null || text.Length <= length)
            {
                return text;
            }

            truncated++;
            return text[..length];
        }

        foreach (var booking in legacy)
        {
            if (
                booking.Id == Guid.Empty
                || booking.StartDateTime is not { } start
                || booking.EndDateTime is not { } end
                || end <= start
                || string.IsNullOrWhiteSpace(booking.FacilityName)
            )
            {
                report.Warnings.Add($"Booking {booking.Id} has no facility, or no time, or ends before it starts, so it was not imported.");
                continue;
            }

            valid.Add(booking);

            var facilityName = booking.FacilityName.Trim();
            if (!facilityIds.TryGetValue(facilityName, out var facilityId))
            {
                // Taken off the sheet since, but its bookings are still history
                var placeholder = new DataFacility { Id = Guid.NewGuid(), TenantId = tenantId, Name = facilityName, Group = RemovedFacilityGroup };
                newFacilities.Add(placeholder);
                facilityIds[facilityName] = facilityId = placeholder.Id;
                unknownFacilities.Add(facilityName);
                report.Facilities.Added++;
            }

            // Bookings with no phone number are all by one member, who is nobody
            var phone = PhoneNumbers.ToStored(booking.UserPhone);
            var memberKey = phone ?? string.Empty;
            if (!members.TryGetValue(memberKey, out var member))
            {
                // Not on the users sheet, as with someone who has left. They are kept as a member who has left, so
                // that what they booked still says who booked it
                member = new TenantMember
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Phone = phone,
                    DisplayName = phone is null ? NobodyName : $"Former member {phone}",
                    Status = MemberStatus.Removed,
                };
                members[memberKey] = member;
                newMembers.Add(member);
                orphaned.Add(phone ?? "(no phone number)");
                report.Members.Added++;
            }

            var startUtc = start.ToUniversalTime();
            var endUtc = end.ToUniversalTime();
            var conduct = Truncate(booking.Conduct, ConductLength) ?? string.Empty;
            var description = Truncate(booking.Description, DescriptionLength);
            var pocName = Truncate(booking.PocName, PocNameLength);
            var pocPhone = Truncate(booking.PocPhone, PocPhoneLength);

            if (!existing.TryGetValue(booking.Id, out var row))
            {
                row = new DataBooking
                {
                    Id = booking.Id,
                    TenantId = tenantId,
                    FacilityId = facilityId,
                    StartUtc = startUtc,
                    EndUtc = endUtc,
                    Conduct = conduct,
                    Description = description,
                    PocName = pocName,
                    PocPhone = pocPhone,
                    BookedByMemberId = member.Id,
                    UnitId = member.UnitId,
                    Revision = 1,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                existing[row.Id] = row;
                added.Add(row);
                report.Bookings.Added++;
                continue;
            }

            var differs =
                row.FacilityId != facilityId
                || row.StartUtc != startUtc
                || row.EndUtc != endUtc
                || row.Conduct != conduct
                || row.Description != description
                || row.PocName != pocName
                || row.PocPhone != pocPhone
                || row.BookedByMemberId != member.Id;
            if (!options.Overwrite || !differs)
            {
                report.Bookings.Unchanged++;
                continue;
            }

            row.FacilityId = facilityId;
            row.StartUtc = startUtc;
            row.EndUtc = endUtc;
            row.Conduct = conduct;
            row.Description = description;
            row.PocName = pocName;
            row.PocPhone = pocPhone;
            row.BookedByMemberId = member.Id;
            row.UnitId = member.UnitId;
            row.UpdatedAt = now;
            changed.Add(row);
            report.Bookings.Updated++;
        }

        if (newFacilities.Count > 0)
        {
            await sql.Insertable(newFacilities).ExecuteCommandAsync(cancellationToken);
        }

        if (newMembers.Count > 0)
        {
            await InsertAsync(newMembers, cancellationToken);
        }

        if (added.Count > 0)
        {
            await InsertAsync(added, cancellationToken);
        }

        if (changed.Count > 0)
        {
            foreach (var chunk in changed.Chunk(Chunk))
            {
                await sql.Updateable(chunk.ToList())
                    .UpdateColumns(b => new { b.FacilityId, b.StartUtc, b.EndUtc, b.Conduct, b.Description, b.PocName, b.PocPhone, b.BookedByMemberId, b.UnitId, b.UpdatedAt })
                    .ExecuteCommandAsync(cancellationToken);
            }
        }

        foreach (var name in unknownFacilities.Order())
        {
            report.Warnings.Add($"Facility '{name}' is not on the Facilities sheet, but has bookings. It was added, and nobody can book it.");
        }

        foreach (var phone in orphaned.Order())
        {
            report.Warnings.Add($"{phone} made bookings but is not on the Users sheet. They were added as a member who has left.");
        }

        if (truncated > 0)
        {
            report.Warnings.Add($"{truncated} booking fields were longer than the database keeps, and were cut short.");
        }

        AddOverlapWarnings(valid, report);
    }

    /// <summary>
    /// Bookings of the same facility that overlap, which the old version could let happen. They are imported
    /// as they are, and nothing new can be booked on top of them.
    /// </summary>
    private static void AddOverlapWarnings(List<Booking> bookings, LegacyImportReport report)
    {
        var overlaps = new List<string>();
        foreach (var group in bookings.GroupBy(b => b.FacilityName!.Trim()))
        {
            Booking? previous = null;
            foreach (var booking in group.OrderBy(b => b.StartDateTime).ThenBy(b => b.EndDateTime))
            {
                if (previous is not null && booking.StartDateTime < previous.EndDateTime)
                {
                    overlaps.Add($"{group.Key}: {previous.Id} and {booking.Id}");
                }

                if (previous is null || booking.EndDateTime > previous.EndDateTime)
                {
                    previous = booking;
                }
            }
        }

        if (overlaps.Count > 0)
        {
            report.Warnings.Add($"{overlaps.Count} bookings overlap another of the same facility, such as {string.Join("; ", overlaps.Take(5))}.");
        }
    }

    private async Task InsertAsync<T>(List<T> rows, CancellationToken cancellationToken)
        where T : class, new()
    {
        foreach (var chunk in rows.Chunk(Chunk))
        {
            await sql.Insertable(chunk.ToList()).ExecuteCommandAsync(cancellationToken);
        }
    }

    internal static NotificationScope ScopeOf(string? group) =>
        Enum.TryParse<NotificationScope>(group, ignoreCase: true, out var scope) ? scope : NotificationScope.None;
}
