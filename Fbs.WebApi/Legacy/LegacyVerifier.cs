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

public sealed class LegacyVerificationReport
{
    private const int MostPerCategory = 20;

    private readonly Dictionary<string, int> _counts = new();

    public List<string> Differences { get; } = [];

    /// <summary>What was compared, for the summary.</summary>
    public Dictionary<string, int> Compared { get; } = new();

    public bool Matches => _counts.Count == 0;

    public int TotalDifferences => _counts.Values.Sum();

    internal void Add(string category, string difference)
    {
        _counts[category] = _counts.GetValueOrDefault(category) + 1;
        if (_counts[category] <= MostPerCategory)
        {
            Differences.Add($"{category}: {difference}");
        }
    }

    public IEnumerable<string> Summary()
    {
        foreach (var (category, count) in Compared)
        {
            yield return $"{category}: {count} compared";
        }

        foreach (var difference in Differences)
        {
            yield return $"Different: {difference}";
        }

        foreach (var (category, count) in _counts.Where(c => c.Value > MostPerCategory))
        {
            yield return $"Different: {category}: and {count - MostPerCategory} more";
        }

        yield return TotalDifferences == 0 ? "The database has everything that is in Google, as it is." : $"{TotalDifferences} differences.";
    }
}

/// <summary>
/// Checks that what is in the database is what is in Google Sheets and Calendar, so that switching over can
/// be done knowing nothing was lost or changed on the way. Only meaningful before the switch: afterwards
/// the database is where changes are made, and Google is not kept up with them.
/// </summary>
public sealed class LegacyVerifier(
    ISqlSugarClient sql,
    IUserRepository users,
    IFacilityRepository facilities,
    INominalRollRepository roster,
    IBookingService legacyBookings
)
{
    public async Task<LegacyVerificationReport> VerifyAsync(string slug, CancellationToken cancellationToken = default)
    {
        var report = new LegacyVerificationReport();
        var tenant = await sql.Queryable<Tenant>().FirstAsync(t => t.Slug == slug, cancellationToken);
        if (tenant is null)
        {
            report.Add("Tenant", $"'{slug}' does not exist.");
            return report;
        }

        var tenantId = tenant.Id;
        var units = (await sql.Queryable<DataUnit>().Where(u => u.TenantId == tenantId).ToListAsync(cancellationToken)).ToDictionary(u => u.Id, u => u.Name);
        var unitIds = units.ToDictionary(u => u.Value, u => u.Key);

        await VerifyMembersAsync(tenantId, units, report, cancellationToken);
        await VerifyFacilitiesAsync(tenantId, units, unitIds, report, cancellationToken);
        await VerifyRosterAsync(tenantId, report, cancellationToken);
        await VerifyBookingsAsync(tenantId, report, cancellationToken);
        return report;
    }

    private async Task VerifyMembersAsync(Guid tenantId, Dictionary<Guid, string> units, LegacyVerificationReport report, CancellationToken cancellationToken)
    {
        var members = (await sql.Queryable<TenantMember>().Where(m => m.TenantId == tenantId && m.Phone != null).ToListAsync(cancellationToken)).ToDictionary(m => m.Phone!);
        var seen = new HashSet<string>();
        foreach (var user in await users.GetListAsync(cancellationToken))
        {
            var phone = PhoneNumbers.ToStored(user.Phone);
            if (phone is null || !seen.Add(phone))
            {
                continue;
            }

            if (!members.TryGetValue(phone, out var member))
            {
                report.Add("Members", $"{phone} ({user.Name}) is not in the database.");
                continue;
            }

            var differences = new List<string>();
            var name = string.IsNullOrWhiteSpace(user.Name) ? phone : user.Name.Trim();
            if (member.DisplayName != name)
            {
                differences.Add($"name is '{member.DisplayName}', not '{name}'");
            }

            var unit = string.IsNullOrWhiteSpace(user.Unit) ? null : user.Unit.Trim();
            var memberUnit = member.UnitId is { } unitId ? units.GetValueOrDefault(unitId) : null;
            if (memberUnit != unit)
            {
                differences.Add($"unit is '{memberUnit}', not '{unit}'");
            }

            if ((member.Role == MemberRole.Admin) != user.IsAdmin)
            {
                differences.Add($"admin is {member.Role == MemberRole.Admin}, not {user.IsAdmin}");
            }

            if (member.NotificationScope != LegacyImporter.ScopeOf(user.NotificationGroup))
            {
                differences.Add($"notifications are {member.NotificationScope}, not {user.NotificationGroup}");
            }

            var chat = string.IsNullOrWhiteSpace(user.TelegramChatId) ? null : user.TelegramChatId.Trim();
            if (member.LegacyChatId != chat)
            {
                differences.Add($"Telegram chat is '{member.LegacyChatId}', not '{chat}'");
            }

            if (differences.Count > 0)
            {
                report.Add("Members", $"{phone} ({user.Name}): {string.Join(", ", differences)}.");
            }
        }

        foreach (var member in members.Values.Where(m => m.Status != MemberStatus.Removed && !seen.Contains(m.Phone!)))
        {
            report.Add("Members", $"{member.Phone} ({member.DisplayName}) is in the database but not on the Users sheet.");
        }

        report.Compared["Members"] = seen.Count;
    }

    private async Task VerifyFacilitiesAsync(
        Guid tenantId,
        Dictionary<Guid, string> units,
        Dictionary<string, Guid> unitIds,
        LegacyVerificationReport report,
        CancellationToken cancellationToken
    )
    {
        var rows = (await sql.Queryable<DataFacility>().Where(f => f.TenantId == tenantId).ToListAsync(cancellationToken)).ToDictionary(f => f.Name);
        var access = (await sql.Queryable<FacilityUnitAccess>().Where(a => a.TenantId == tenantId).ToListAsync(cancellationToken))
            .GroupBy(a => a.FacilityId)
            .ToDictionary(g => g.Key, g => g.Select(a => units.GetValueOrDefault(a.UnitId)).OfType<string>().ToHashSet());
        var seen = new HashSet<string>();

        foreach (var facility in await facilities.GetListAsync(cancellationToken))
        {
            var name = facility.Name?.Trim();
            if (string.IsNullOrEmpty(name) || !seen.Add(name))
            {
                continue;
            }

            if (!rows.TryGetValue(name, out var row))
            {
                report.Add("Facilities", $"'{name}' is not in the database.");
                continue;
            }

            var scope = facility.Scope ?? [];
            var everyone = scope.Contains("All");
            var wantedUnits = everyone ? [] : scope.Select(s => s.Trim()).Where(unitIds.ContainsKey).ToHashSet();
            var actualUnits = access.GetValueOrDefault(row.Id) ?? [];
            var group = facility.Group?.Trim();
            if (row.Group != group || row.AvailableToAll != everyone || !actualUnits.SetEquals(wantedUnits))
            {
                report.Add(
                    "Facilities",
                    $"'{name}' is in group '{row.Group}' for {(row.AvailableToAll ? "everyone" : string.Join(", ", actualUnits.Order()))}, not '{group}' for {(everyone ? "everyone" : string.Join(", ", wantedUnits.Order()))}."
                );
            }
        }

        foreach (var row in rows.Values.Where(f => !seen.Contains(f.Name) && f.Group != "Removed"))
        {
            report.Add("Facilities", $"'{row.Name}' is in the database but not on the Facilities sheet.");
        }

        report.Compared["Facilities"] = seen.Count;
    }

    private async Task VerifyRosterAsync(Guid tenantId, LegacyVerificationReport report, CancellationToken cancellationToken)
    {
        var rows = (await sql.Queryable<RosterEntry>().Where(e => e.TenantId == tenantId).ToListAsync(cancellationToken)).ToDictionary(e => e.Phone);
        var seen = new HashSet<string>();
        foreach (var entry in await roster.GetListAsync(cancellationToken))
        {
            var phone = PhoneNumbers.ToStored(entry.Phone);
            if (phone is null || !seen.Add(phone))
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(entry.Name) ? phone : entry.Name.Trim();
            var unit = string.IsNullOrWhiteSpace(entry.Unit) ? null : entry.Unit.Trim();
            if (!rows.TryGetValue(phone, out var row))
            {
                report.Add("Roster", $"{phone} ({entry.Name}) is not in the database.");
            }
            else if (row.Name != name || row.Unit != unit)
            {
                report.Add("Roster", $"{phone} is '{row.Name}' of '{row.Unit}', not '{name}' of '{unit}'.");
            }
        }

        foreach (var row in rows.Values.Where(r => !seen.Contains(r.Phone)))
        {
            report.Add("Roster", $"{row.Phone} ({row.Name}) is in the database but not on the Nominal Roll sheet.");
        }

        report.Compared["Roster"] = seen.Count;
    }

    private async Task VerifyBookingsAsync(Guid tenantId, LegacyVerificationReport report, CancellationToken cancellationToken)
    {
        var facilityNames = (await sql.Queryable<DataFacility>().Where(f => f.TenantId == tenantId).ToListAsync(cancellationToken)).ToDictionary(f => f.Id, f => f.Name);
        var memberPhones = (await sql.Queryable<TenantMember>().Where(m => m.TenantId == tenantId).ToListAsync(cancellationToken)).ToDictionary(m => m.Id, m => m.Phone);
        var rows = (await sql.Queryable<DataBooking>().Where(b => b.TenantId == tenantId).ToListAsync(cancellationToken)).ToDictionary(b => b.Id);
        var seen = new HashSet<Guid>();

        foreach (var booking in await legacyBookings.ListAsync(cancellationToken))
        {
            if (booking.Id == Guid.Empty || booking.StartDateTime is null || booking.EndDateTime is null || string.IsNullOrWhiteSpace(booking.FacilityName) || booking.EndDateTime <= booking.StartDateTime)
            {
                continue;
            }

            seen.Add(booking.Id);
            if (!rows.TryGetValue(booking.Id, out var row))
            {
                report.Add("Bookings", $"{booking.Id} ({booking.FacilityName}, {booking.StartDateTime:u}) is not in the database.");
                continue;
            }

            var differences = new List<string>();
            if (row.CancelledAt is not null)
            {
                differences.Add("is cancelled");
            }

            if (facilityNames.GetValueOrDefault(row.FacilityId) != booking.FacilityName.Trim())
            {
                differences.Add($"facility is '{facilityNames.GetValueOrDefault(row.FacilityId)}', not '{booking.FacilityName.Trim()}'");
            }

            if (row.StartUtc != booking.StartDateTime.Value || row.EndUtc != booking.EndDateTime.Value)
            {
                differences.Add($"time is {row.StartUtc:u} to {row.EndUtc:u}, not {booking.StartDateTime.Value.ToUniversalTime():u} to {booking.EndDateTime.Value.ToUniversalTime():u}");
            }

            if (row.Conduct != Cut(booking.Conduct, 200) && !(row.Conduct == string.Empty && booking.Conduct is null))
            {
                differences.Add("conduct differs");
            }

            if (row.Description != Cut(booking.Description, 2000) || row.PocName != Cut(booking.PocName, 200) || row.PocPhone != Cut(booking.PocPhone, 32))
            {
                differences.Add("description or point of contact differs");
            }

            if (memberPhones.GetValueOrDefault(row.BookedByMemberId) != PhoneNumbers.ToStored(booking.UserPhone))
            {
                differences.Add($"booked by {memberPhones.GetValueOrDefault(row.BookedByMemberId)}, not {PhoneNumbers.ToStored(booking.UserPhone)}");
            }

            if (differences.Count > 0)
            {
                report.Add("Bookings", $"{booking.Id}: {string.Join(", ", differences)}.");
            }
        }

        foreach (var row in rows.Values.Where(b => b.CancelledAt is null && !seen.Contains(b.Id)))
        {
            report.Add("Bookings", $"{row.Id} is in the database but not in the calendar.");
        }

        report.Compared["Bookings"] = seen.Count;
    }

    private static string? Cut(string? text, int length) => text is null || text.Length <= length ? text : text[..length];
}
