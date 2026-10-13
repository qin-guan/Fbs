using Fbs.WebApi.Tests.Data;

namespace Fbs.WebApi.Tests.Helpers;

public static class StoredBookingAssertions
{
    /// <summary>That this many bookings are stored, so a booking that is meant to be gone, or to be there, is.</summary>
    public static async Task AssertStoredBookingsAsync(this DatabaseFbsApiFactory factory, int expected) =>
        await Assert.That(factory.StoredBookingCount).IsEqualTo(expected);
}
