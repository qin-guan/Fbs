using System.Text;
using Fbs.WebApi.Options;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Microsoft.Extensions.Options;

namespace Fbs.WebApi.CalendarSync;

/// <summary>Google Calendar, which bookings are copied to for the organisations that connect a calendar.</summary>
public static class GoogleCalendar
{
    /// <summary>The client, made when first used from the service account in <see cref="GoogleOptions"/>.</summary>
    public static IServiceCollection AddGoogleCalendar(this IServiceCollection services) =>
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
            return new CalendarService(
                new BaseClientService.Initializer { HttpClientInitializer = credential }
            );
        });
}
