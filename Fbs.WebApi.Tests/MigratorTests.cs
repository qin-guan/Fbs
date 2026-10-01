extern alias Migrator;

using System.Diagnostics;
using Fbs.WebApi.Data;
using Fbs.WebApi.Tests.Data;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using ApplyCommand = Migrator::Fbs.DbMigrator.Commands.ApplyCommand;
using DatabaseTarget = Migrator::Fbs.DbMigrator.Commands.DatabaseTarget;
using DiffCommand = Migrator::Fbs.DbMigrator.Commands.DiffCommand;

namespace Fbs.WebApi.Tests;

public class MigratorTests
{
    private static (DiffCommand Diff, ApplyCommand Apply) Commands(TestDatabase database)
    {
        var db = database.CreateClient();
        return (
            new DiffCommand(NullLogger<DiffCommand>.Instance, db),
            new ApplyCommand(NullLogger<ApplyCommand>.Instance, db, new DatabaseTarget(database.ConnectionString))
        );
    }

    [Test]
    public async Task Diff_says_there_is_something_to_apply_until_it_has_been()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (diff, apply) = Commands(database);

        await Assert.That(await diff.Diff(default)).IsEqualTo(2);
        await apply.Apply();
        await Assert.That(await diff.Diff(default)).IsEqualTo(0);
    }

    [Test]
    public async Task Applying_twice_changes_nothing_the_second_time()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (_, apply) = Commands(database);

        await apply.Apply();
        await apply.Apply();

        await Assert.That(SchemaValidator.FindProblems(database.CreateClient())).IsEmpty();
    }

    [Test]
    public async Task Applying_puts_back_an_index_that_was_dropped()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (diff, apply) = Commands(database);
        await apply.Apply();
        await database.CreateClient().Ado.ExecuteCommandAsync("DROP INDEX UX_Facility_TenantId_Name ON Facility");

        await Assert.That(await diff.Diff(default)).IsEqualTo(2);
        await apply.Apply();

        await Assert.That(await diff.Diff(default)).IsEqualTo(0);
    }

    [Test]
    public async Task Applying_refuses_to_drop_a_column_unless_allowed()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (_, apply) = Commands(database);
        await apply.Apply();
        var db = database.CreateClient();
        await db.Ado.ExecuteCommandAsync("ALTER TABLE Facility ADD COLUMN Retired INT NULL");

        await Assert.That(async () => await apply.Apply()).Throws<InvalidOperationException>();
        await Assert.That(HasColumn(db, "Facility", "Retired")).IsTrue();

        await apply.Apply(allowDestructive: true);
        await Assert.That(HasColumn(db, "Facility", "Retired")).IsFalse();
    }

    [Test]
    public async Task Applying_can_create_the_database_first()
    {
        var name = $"fbs_test_{Guid.NewGuid():N}";
        var server = await TestDatabase.ServerAsync();
        var connectionString = new MySqlConnectionStringBuilder(server) { Database = name }.ConnectionString;
        var db = SqlSugarClientFactory.Create(connectionString);
        var apply = new ApplyCommand(NullLogger<ApplyCommand>.Instance, db, new DatabaseTarget(connectionString));
        try
        {
            await apply.Apply(createDatabase: true);

            await Assert.That(SchemaValidator.FindProblems(db)).IsEmpty();
        }
        finally
        {
            await using var connection = new MySqlConnection(server);
            await connection.OpenAsync();
            await new MySqlCommand($"DROP DATABASE IF EXISTS `{name}`", connection).ExecuteNonQueryAsync();
        }
    }

    // What a pipeline sees: the exit code, and where the connection string comes from

    [Test]
    public async Task The_exit_codes_tell_a_pipeline_what_happened()
    {
        await using var database = await TestDatabase.CreateAsync();

        await Assert.That((await RunAsync(database.ConnectionString, "diff")).ExitCode).IsEqualTo(2);
        await Assert.That((await RunAsync(database.ConnectionString, "apply")).ExitCode).IsEqualTo(0);
        await Assert.That((await RunAsync(database.ConnectionString, "diff")).ExitCode).IsEqualTo(0);
    }

    [Test]
    public async Task A_destructive_change_fails_the_run_until_it_is_allowed()
    {
        await using var database = await TestDatabase.CreateAsync();
        await RunAsync(database.ConnectionString, "apply");
        await database.CreateClient().Ado.ExecuteCommandAsync("ALTER TABLE Facility ADD COLUMN Retired INT NULL");

        var refused = await RunAsync(database.ConnectionString, "apply");
        var allowed = await RunAsync(database.ConnectionString, "apply", "--allow-destructive");

        await Assert.That(refused.ExitCode).IsNotEqualTo(0);
        await Assert.That(refused.Output).Contains("--allow-destructive");
        await Assert.That(allowed.ExitCode).IsEqualTo(0);
    }

    [Test]
    public async Task Running_without_a_connection_string_fails_and_says_why()
    {
        var result = await RunAsync(connectionString: null, "diff");

        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(result.Output).Contains("ConnectionStrings:db is required");
    }

    [Test]
    public async Task The_commands_that_read_google_say_what_is_missing_when_it_is_not_set()
    {
        await using var database = await TestDatabase.CreateAsync();
        await RunAsync(database.ConnectionString, "apply");

        foreach (var command in new[] { "import-legacy", "verify-legacy", "export-legacy" })
        {
            var result = await RunAsync(database.ConnectionString, command);

            await Assert.That(result.ExitCode).IsNotEqualTo(0);
            await Assert.That(result.Output).Contains("Google:ServiceAccountJsonCredential");
        }
    }

    [Test]
    public async Task The_commands_that_do_not_read_google_do_not_need_it()
    {
        await using var database = await TestDatabase.CreateAsync();

        // Nothing about Google is set, and the schema is still applied
        await Assert.That((await RunAsync(database.ConnectionString, "apply")).ExitCode).IsEqualTo(0);
        await Assert.That((await RunAsync(database.ConnectionString, "diff")).ExitCode).IsEqualTo(0);
    }

    [Test]
    public async Task The_import_commands_are_listed()
    {
        await using var database = await TestDatabase.CreateAsync();

        var result = await RunAsync(database.ConnectionString, "--help");

        await Assert.That(result.Output).Contains("import-legacy");
        await Assert.That(result.Output).Contains("verify-legacy");
        await Assert.That(result.Output).Contains("export-legacy");
    }

    private static bool HasColumn(SqlSugar.ISqlSugarClient db, string table, string column) =>
        db.DbMaintenance.GetColumnInfosByTableName(table, false).Any(c => c.DbColumnName == column);

    private static async Task<(int ExitCode, string Output)> RunAsync(string? connectionString, params string[] arguments)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "Fbs.DbMigrator.dll");
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(dll);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment.Remove("ConnectionStrings__db");
        if (connectionString is not null)
        {
            start.Environment["ConnectionStrings__db"] = connectionString;
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output + await error);
    }
}
