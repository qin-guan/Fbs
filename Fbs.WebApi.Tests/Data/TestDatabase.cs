extern alias AppHost;
using Aspire.Hosting.Testing;
using Fbs.WebApi.Data;
using MySqlConnector;
using SqlSugar;

namespace Fbs.WebApi.Tests.Data;

/// <summary>
/// A database of its own for a test run, on the TiDB that the AppHost starts, in a container that is removed
/// when the tests end. Docker is all it needs.
/// </summary>
/// <remarks>
/// Tests run against TiDB rather than MySQL, as that is what production uses and the two differ in how
/// transactions read (see docs/adr/spikes/s3-tidb-concurrency).
/// </remarks>
public sealed class TestDatabase : IAsyncDisposable
{
    private static readonly Lazy<Task<string>> Server = new(StartServerAsync);

    private readonly string _serverConnectionString;

    private TestDatabase(string serverConnectionString, string name)
    {
        _serverConnectionString = serverConnectionString;
        Name = name;
        ConnectionString = new MySqlConnectionStringBuilder(serverConnectionString) { Database = name }.ConnectionString;
    }

    public string Name { get; }

    /// <summary>The connection string of the database created for this run.</summary>
    public string ConnectionString { get; }

    /// <summary>The connection string of the TiDB server, without a database. It is started the first time it is asked for.</summary>
    public static Task<string> ServerAsync() => Server.Value;

    /// <summary>Creates an empty database with a unique name.</summary>
    public static async Task<TestDatabase> CreateAsync()
    {
        var database = new TestDatabase(await ServerAsync(), $"fbs_test_{Guid.NewGuid():N}");
        await database.ExecuteOnServerAsync($"CREATE DATABASE `{database.Name}`");
        return database;
    }

    /// <summary>A SqlSugar client the way the API creates it.</summary>
    public SqlSugarScope CreateClient() => SqlSugarClientFactory.Create(ConnectionString);

    public async ValueTask DisposeAsync()
    {
        await ExecuteOnServerAsync($"DROP DATABASE IF EXISTS `{Name}`");
    }

    private async Task ExecuteOnServerAsync(string sql)
    {
        await using var connection = new MySqlConnection(_serverConnectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Starts the AppHost with only the database, as the API is what the tests start themselves. Starting sometimes fails
    /// before anything has run (Aspire has not been given an address for a container's port yet), so it is tried again a
    /// couple of times rather than failing every test that needs the database.
    /// </summary>
    private static async Task<string> StartServerAsync()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await StartOnceAsync();
            }
            catch (Exception) when (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    private static async Task<string> StartOnceAsync()
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<AppHost::Projects.Fbs_AppHost>([], (options, _) => options.DisableDashboard = true);
        foreach (var resource in builder.Resources.Where(r => r.Name is not ("tidb" or "db")).ToList())
        {
            builder.Resources.Remove(resource);
        }

        var app = await builder.BuildAsync();
        try
        {
            await app.StartAsync();
            await app.ResourceNotifications.WaitForResourceHealthyAsync("tidb");
            // Without the database the AppHost names, which is not there
            return new MySqlConnectionStringBuilder(await app.GetConnectionStringAsync("db")) { Database = "" }.ConnectionString;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }
}
