using MySqlConnector;

namespace Fbs.DbMigrator.Commands;

/// <summary>
/// The database the migrator works on, from <c>ConnectionStrings__db</c>.
/// </summary>
public sealed class DatabaseTarget(string connectionString)
{
    public string Name => new MySqlConnectionStringBuilder(connectionString).Database;

    /// <summary>Creates the database if it isn't there, for when it is run against a fresh server.</summary>
    public async Task CreateIfMissingAsync(CancellationToken cancellationToken)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.Database))
        {
            throw new InvalidOperationException("The connection string has to name the database to create.");
        }

        var name = builder.Database;
        builder.Database = string.Empty;

        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand($"CREATE DATABASE IF NOT EXISTS `{name.Replace("`", "``")}`", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
