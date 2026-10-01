using Fbs.WebApi.Data;
using Fbs.WebApi.Tests.Data;

namespace Fbs.WebApi.Tests;

/// <summary>
/// A version whose schema hasn't been applied yet has to fail to start, so a deploy that is waiting on
/// its health check never takes traffic.
/// </summary>
public class StartupTests
{
    private static FbsApiFactory Factory(string connectionString, bool? validate = null) =>
        new FbsApiFactoryWithDatabase(connectionString, validate);

    [Test]
    public async Task The_api_does_not_start_against_a_database_that_is_missing_the_schema()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var factory = Factory(database.ConnectionString);

        var exception = await Assert.That(() => factory.CreateClient()).Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("Fbs.DbMigrator");
        await Assert.That(exception.Message).Contains("Table Booking is missing");
    }

    [Test]
    public async Task The_api_starts_once_the_schema_has_been_applied()
    {
        await using var database = await TestDatabase.CreateAsync();
        database.CreateClient().CodeFirst.InitTables(SchemaDifferenceInspector.GetEntityTypes());
        await using var factory = Factory(database.ConnectionString);

        using var client = factory.CreateClient();

        (await client.GetAsync("/health")).EnsureSuccessStatusCode();
    }

    [Test]
    public async Task The_check_can_be_turned_off()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var factory = Factory(database.ConnectionString, validate: false);

        using var client = factory.CreateClient();

        (await client.GetAsync("/health")).EnsureSuccessStatusCode();
    }

    private sealed class FbsApiFactoryWithDatabase(string connectionString, bool? validate) : FbsApiFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("ConnectionStrings:db", connectionString);
            if (validate is { } value)
            {
                builder.UseSetting("Startup:ValidateDatabaseSchema", value.ToString());
            }
        }
    }
}
