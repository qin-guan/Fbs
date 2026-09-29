using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Repository;
using SqlSugar;
using Booking = Fbs.WebApi.Entities.Booking;
using DataBooking = Fbs.WebApi.Data.Entities.Booking;
using DataFacility = Fbs.WebApi.Data.Entities.Facility;

namespace Fbs.WebApi.Legacy;

public sealed class LegacyExportReport
{
    public bool DryRun { get; init; }

    public ImportCounts Bookings { get; } = new();

    /// <summary>Bookings whose events were removed because they were cancelled in the database.</summary>
    public int Removed { get; set; }

    /// <summary>What could not be written back, which the calendar still doesn't have as it is in the database.</summary>
    public List<string> Failures { get; } = [];

    public IEnumerable<string> Summary()
    {
        yield return DryRun ? "Dry run: nothing was written." : "Written back.";
        yield return $"Bookings: {Bookings}, {Removed} removed";
        foreach (var failure in Failures)
        {
            yield return $"Failed: {failure}";
        }
    }
}

/// <summary>
/// Writes what was booked, changed and cancelled in the database back to Google Calendar, in the form the old
/// version reads, so that going back to it after the switch doesn't lose what was done in between.
/// </summary>
/// <remarks>
/// Only bookings go back. Users, facilities and the roster are still on the sheets, which are where they are
/// edited, and who has linked a Telegram chat, or been made an admin, since the switch is not.
/// A booking that can't be written, such as one by someone not on the Users sheet, or with more written on
/// it than an event can hold, is listed and the rest are still written. Running it again writes only what
/// is still different.
/// </remarks>
public sealed class LegacyExporter(ISqlSugarClient sql, BookingRepository legacy)
{
    public async Task<LegacyExportReport> ExportAsync(string slug, bool dryRun = false, CancellationToken cancellationToken = default)
    {
        var report = new LegacyExportReport { DryRun = dryRun };
        var tenant =
            await sql.Queryable<Tenant>().FirstAsync(t => t.Slug == slug, cancellationToken)
            ?? throw new InvalidOperationException($"There is no tenant with the slug '{slug}'.");
        var tenantId = tenant.Id;

        var facilities = (await sql.Queryable<DataFacility>().Where(f => f.TenantId == tenantId).ToListAsync(cancellationToken)).ToDictionary(f => f.Id, f => f.Name);
        var phones = (await sql.Queryable<TenantMember>().Where(m => m.TenantId == tenantId).ToListAsync(cancellationToken)).ToDictionary(m => m.Id, m => PhoneNumbers.ToApi(m.Phone));
        var rows = await sql.Queryable<DataBooking>().Where(b => b.TenantId == tenantId).OrderBy(b => b.StartUtc).ToListAsync(cancellationToken);
        var existing = (await legacy.GetLatestListAsync(cancellationToken)).ToDictionary(b => b.Id);

        foreach (var row in rows)
        {
            if (row.CancelledAt is not null)
            {
                // Cancelled in the database, and still in the calendar
                if (existing.ContainsKey(row.Id))
                {
                    await TryAsync(report, row, async () =>
                    {
                        if (!dryRun)
                        {
                            await legacy.RemoveEventsAsync(row.Id, cancellationToken);
                        }

                        report.Removed++;
                    });
                }

                continue;
            }

            var booking = new Booking
            {
                Id = row.Id,
                FacilityName = facilities.GetValueOrDefault(row.FacilityId),
                StartDateTime = row.StartUtc,
                EndDateTime = row.EndUtc,
                Conduct = row.Conduct,
                Description = row.Description,
                PocName = row.PocName,
                PocPhone = row.PocPhone,
                UserPhone = phones.GetValueOrDefault(row.BookedByMemberId),
            };

            if (!existing.TryGetValue(row.Id, out var current))
            {
                await TryAsync(report, row, async () =>
                {
                    if (!dryRun)
                    {
                        await legacy.InsertAsync(booking, cancellationToken);
                    }

                    report.Bookings.Added++;
                });
            }
            else if (Differs(current, booking))
            {
                await TryAsync(report, row, async () =>
                {
                    if (!dryRun)
                    {
                        await legacy.UpdateAsync(booking, cancellationToken);
                    }

                    report.Bookings.Updated++;
                });
            }
            else
            {
                report.Bookings.Unchanged++;
            }
        }

        return report;
    }

    private static async Task TryAsync(LegacyExportReport report, DataBooking row, Func<Task> write)
    {
        try
        {
            await write();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            report.Failures.Add($"{row.Id} ({row.StartUtc:u}): {e.Message}");
        }
    }

    private static bool Differs(Booking legacy, Booking booking) =>
        legacy.FacilityName != booking.FacilityName
        || legacy.StartDateTime != booking.StartDateTime
        || legacy.EndDateTime != booking.EndDateTime
        || (legacy.Conduct ?? string.Empty) != (booking.Conduct ?? string.Empty)
        || legacy.Description != booking.Description
        || legacy.PocName != booking.PocName
        || legacy.PocPhone != booking.PocPhone
        || legacy.UserPhone != booking.UserPhone;
}
