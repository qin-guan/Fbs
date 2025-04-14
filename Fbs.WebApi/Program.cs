using FastEndpoints;
using FastEndpoints.Security;
using Fbs.WebApi.Options;
using Fbs.WebApi.Services.GoogleSheets;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Microsoft.Extensions.Options;
using Telegram.Bot;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

#region Options

builder.Services.AddOptions<GoogleOptions>()
    .Bind(builder.Configuration.GetSection("Google"))
    .Validate(options =>
        !string.IsNullOrWhiteSpace(options.ServiceAccountJsonCredential)
    );

builder.Services.AddOptions<TelegramOptions>()
    .Bind(builder.Configuration.GetSection("Telegram"))
    .Validate(options =>
        !string.IsNullOrWhiteSpace(options.Token) &&
        !string.IsNullOrWhiteSpace(options.WebhookUrl)
    );

#endregion

#region Services

builder.Services.AddAuthenticationCookie(validFor: TimeSpan.FromDays(1))
    .AddAuthorization();

builder.Services.AddHttpClient<TelegramBotClient>("tgwebhook")
    .AddTypedClient((httpClient, sp) =>
        new TelegramBotClient(
            sp.GetRequiredService<IOptions<TelegramOptions>>().Value.Token,
            httpClient
        )
    );

builder.Services.AddScoped<GoogleSheetsOtpService>();
builder.Services.AddScoped<GoogleSheetsUsersService>();
builder.Services.AddScoped<GoogleSheetsFacilitiesService>();

builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<GoogleOptions>>();

    var credential = GoogleCredential.FromJson(options.Value.ServiceAccountJsonCredential);
    var service = new SheetsService(new BaseClientService.Initializer
    {
        HttpClientInitializer = credential
    });

    return service;
});

builder.Services.AddFastEndpoints();
builder.Services.AddOpenApi();

#endregion

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var client = scope.ServiceProvider.GetRequiredService<TelegramBotClient>();
    var options = scope.ServiceProvider.GetRequiredService<IOptions<TelegramOptions>>();
    await client.SetWebhook(options.Value.WebhookUrl);
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseFastEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapDefaultEndpoints();

app.Run();