using FastEndpoints;
using FastEndpoints.Security;
using FastEndpoints.Swagger;
using Fbs.WebApi;
using Fbs.WebApi.EventListeners;
using Fbs.WebApi.Options;
using Fbs.WebApi.Repository;
using Fbs.WebApi.Types;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Telegram.Bot;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddGraphQL()
    .AddDiagnosticEventListener<ExceptionEventListener>()
    .ModifyRequestOptions(options => { options.IncludeExceptionDetails = true; })
    .AddAuthorization()
    .AddTypes()
    .AddMutationType<Mutation>();

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

builder.Services.AddAuthenticationCookie(validFor: TimeSpan.FromDays(1), options =>
    {
        if (!builder.Environment.IsProduction()) return;
        
        options.Cookie.Domain = ".from.sg";
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    })
    .AddAuthorization();

builder.Services.AddHttpClient<TelegramBotClient>("tgwebhook")
    .AddTypedClient((httpClient, sp) =>
        new TelegramBotClient(
            sp.GetRequiredService<IOptions<TelegramOptions>>().Value.Token,
            httpClient
        )
    );

builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<GoogleOptions>>();

    var credential = GoogleCredential.FromJson(options.Value.ServiceAccountJsonCredential).CreateScoped([
        "https://www.googleapis.com/auth/calendar",
        "https://www.googleapis.com/auth/calendar.events",
    ]);
    var service = new CalendarService(new BaseClientService.Initializer
    {
        HttpClientInitializer = credential
    });

    return service;
});

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

builder.Services.AddSingleton<InstrumentationSource>();

builder.Services.AddScoped<FacilityRepository>();
builder.Services.AddScoped<OtpRepository>();
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<BookingRepository>();

builder.Services.AddFastEndpoints();
builder.Services.SwaggerDocument(options =>
{
    options.EndpointFilter = ep => ep.EndpointTags?.Contains("Telegram") is false or null;
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.WithOrigins("http://localhost:3000");
        }
        else
        {
            policy.WithOrigins("https://*.3sib-fbs.pages.dev");
            policy.WithOrigins("https://3sib-fbs.pages.dev");
            policy.WithOrigins("https://*.3sib-fbs.from.sg");
            policy.WithOrigins("https://3sib-fbs.from.sg");
        }

        policy
            .AllowCredentials()
            .AllowAnyHeader()
            .AllowAnyMethod()
            .SetIsOriginAllowedToAllowWildcardSubdomains();
    });
});

#endregion

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var client = scope.ServiceProvider.GetRequiredService<TelegramBotClient>();
    var options = scope.ServiceProvider.GetRequiredService<IOptions<TelegramOptions>>();
    await client.SetWebhook(options.Value.WebhookUrl);
}

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.UseFastEndpoints(config => config.Errors.UseProblemDetails(c => { c.IndicateErrorCode = true; }));
app.UseSwaggerGen(config => { config.Path = "/openapi/{documentName}.json"; });

app.MapDefaultEndpoints();
app.MapScalarApiReference();

app.MapGraphQL();

app.RunWithGraphQLCommands(args);