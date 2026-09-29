using System.Text;
using Fbs.WebApi.Bookings;
using Fbs.WebApi.Options;
using Fbs.WebApi.Repository;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Fbs.WebApi.Legacy;

/// <summary>
/// What is needed to use Google Calendar and Sheets, and to keep bookings, users, facilities and the roster
/// in them, as they were before they were in the database. Shared by the API and by the importer that moves
/// them, so both read them the same way.
/// </summary>
public static class GoogleServices
{
    /// <summary>The clients, made when first used from the service account in <see cref="GoogleOptions"/>.</summary>
    public static IServiceCollection AddGoogleClients(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<GoogleOptions>>();
            var serviceAccountJsonCredential = Encoding.UTF8.GetString(
                Convert.FromBase64String(options.Value.ServiceAccountJsonCredential)
            );

            var credential = GoogleCredential
                .FromJson(serviceAccountJsonCredential)
                .CreateScoped(
                    "https://www.googleapis.com/auth/calendar",
                    "https://www.googleapis.com/auth/calendar.events"
                );
            var service = new CalendarService(
                new BaseClientService.Initializer { HttpClientInitializer = credential }
            );

            return service;
        });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<GoogleOptions>>();
            var serviceAccountJsonCredential = Encoding.UTF8.GetString(
                Convert.FromBase64String(options.Value.ServiceAccountJsonCredential)
            );

            var credential = GoogleCredential.FromJson(serviceAccountJsonCredential);
            var service = new SheetsService(
                new BaseClientService.Initializer { HttpClientInitializer = credential }
            );

            return service;
        });

        return services;
    }

    /// <summary>
    /// Bookings in Google Calendar, and users, facilities, the roster and login codes in Google Sheets. Needs
    /// <see cref="AddGoogleClients"/> and a <c>HybridCache</c>.
    /// </summary>
    public static IServiceCollection AddGoogleStorage(this IServiceCollection services)
    {
        services.TryAddSingleton<InstrumentationSource>();
        services.AddSingleton<BookingCache>();
        services.AddSingleton<BookingWriteLock>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IFacilityRepository, FacilityRepository>();
        services.AddScoped<INominalRollRepository, NominalRollRepository>();
        services.AddScoped<IOtpRepository, OtpRepository>();
        services.AddScoped<BookingRepository>();
        services.AddScoped<IBookingService, CalendarBookingService>();

        return services;
    }
}
