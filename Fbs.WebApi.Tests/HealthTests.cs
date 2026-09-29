using System.Net;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;

namespace Fbs.WebApi.Tests;

/// <summary>
/// The hosting platform calls /health from inside the container to know when a new version is
/// ready, so it has to answer in production, and without signing in.
/// </summary>
public class HealthTests
{
    [ClassDataSource<ProductionFbsApiFactory>]
    public required ProductionFbsApiFactory Factory { get; init; }

    [Test]
    public async Task Health_is_healthy_in_production_without_signing_in()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync("/health");

        await Assert.That(response).HasStatus(HttpStatusCode.OK);
        // Only the status, nothing about what was checked
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("Healthy");
    }

    [Test]
    public async Task Only_health_is_exposed_in_production()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync("/alive");

        await Assert.That(response).HasStatus(HttpStatusCode.NotFound);
    }
}

/// <summary>A new version that can't reach the database isn't ready to take over from the one that is running.</summary>
public class DatabaseHealthTests
{
    private sealed class FactoryWithConnectionString(string connectionString) : ProductionFbsApiFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("ConnectionStrings:db", connectionString);
            // What is being tested is that it can be reached, not that its schema is there
            builder.UseSetting("Startup:ValidateDatabaseSchema", "false");
        }
    }

    [Test]
    public async Task It_is_healthy_when_the_database_answers()
    {
        var database = await TestDatabase.SharedAsync();
        await using var factory = new FactoryWithConnectionString(database.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        await Assert.That(response).HasStatus(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("Healthy");
    }

    [Test]
    public async Task It_is_unhealthy_when_the_database_does_not_answer_and_says_nothing_more()
    {
        // Nothing is listening there
        await using var factory = new FactoryWithConnectionString("Server=127.0.0.1;Port=1;User ID=root;Database=fbs;Connection Timeout=2");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        await Assert.That(response).HasStatus(HttpStatusCode.ServiceUnavailable);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("Unhealthy");
    }
}
