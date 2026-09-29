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
