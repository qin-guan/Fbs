using System.Text;

namespace Fbs.WebApi.Auth.WorkOS;

/// <summary>Somebody in Clerk's export of its users.</summary>
/// <param name="PasswordDigest">The hash of their password, if they have one: somebody who only signs in with Google has none.</param>
/// <param name="PasswordHasher">What made <paramref name="PasswordDigest"/>: <c>bcrypt</c> for anybody who set their password with Clerk.</param>
/// <param name="HasTotp">Whether they had an authenticator app as a second step, which can't be moved.</param>
public sealed record ClerkExportedUser(
    string Id,
    string? FirstName,
    string? LastName,
    string? PrimaryEmail,
    IReadOnlyList<string> VerifiedEmails,
    IReadOnlyList<string> UnverifiedEmails,
    string? PasswordDigest,
    string? PasswordHasher,
    bool HasTotp
)
{
    /// <summary>
    /// Whether Clerk says their primary email address was never verified. Clerk is set up so nobody can sign up without verifying it
    /// (ADR 0001), so one that is in neither list was verified: only one Clerk lists as unverified is taken not to be.
    /// </summary>
    public bool PrimaryEmailUnverified =>
        PrimaryEmail is not null
        && UnverifiedEmails.Contains(PrimaryEmail, StringComparer.OrdinalIgnoreCase)
        && !VerifiedEmails.Contains(PrimaryEmail, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Reads the CSV of users that Clerk's dashboard exports (Settings, then User exports): <c>id</c>, <c>first_name</c>, <c>last_name</c>,
/// <c>primary_email_address</c>, <c>verified_email_addresses</c>, <c>unverified_email_addresses</c>, <c>totp_secret</c>,
/// <c>password_digest</c> and <c>password_hasher</c>, among others that aren't needed.
/// </summary>
public static class ClerkExport
{
    private static readonly string[] Required = ["id", "primary_email_address"];

    public static async Task<IReadOnlyList<ClerkExportedUser>> ReadCsvAsync(string path, CancellationToken ct)
    {
        // Detects a byte order mark, which a spreadsheet that has saved it may have added
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return ReadCsv(await reader.ReadToEndAsync(ct));
    }

    public static IReadOnlyList<ClerkExportedUser> ReadCsv(string csv)
    {
        var rows = ParseCsv(csv.TrimStart('﻿')).Where(row => row.Any(field => field.Length > 0)).ToList();
        if (rows.Count == 0)
        {
            throw new FormatException("The export is empty.");
        }

        var header = rows[0].Select(name => name.Trim().ToLowerInvariant()).ToList();
        var missing = Required.Where(name => !header.Contains(name)).ToList();
        if (missing.Count > 0)
        {
            throw new FormatException($"This is not Clerk's export of users: it has no {string.Join(" or ", missing)} column.");
        }

        string? Field(IReadOnlyList<string> row, string name)
        {
            var index = header.IndexOf(name);
            return index < 0 || index >= row.Count || string.IsNullOrWhiteSpace(row[index]) ? null : row[index].Trim();
        }

        return rows.Skip(1)
            .Select(row => new ClerkExportedUser(
                Field(row, "id") ?? "",
                Field(row, "first_name"),
                Field(row, "last_name"),
                Field(row, "primary_email_address"),
                List(Field(row, "verified_email_addresses")),
                List(Field(row, "unverified_email_addresses")),
                Field(row, "password_digest"),
                Field(row, "password_hasher"),
                Field(row, "totp_secret") is not null
            ))
            .ToList();
    }

    /// <summary>Several addresses in one field, which exports have separated with any of these.</summary>
    private static List<string> List(string? value) =>
        value is null ? [] : value.Split(['|', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>RFC 4180: fields separated by commas, in double quotes if they have commas, quotes or line breaks in them, with a quote in one doubled.</summary>
    private static IEnumerable<List<string>> ParseCsv(string csv)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < csv.Length && csv[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0:
                    quoted = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    yield return row;
                    row = [];
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (quoted)
        {
            throw new FormatException("The export ends inside a quoted field.");
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            yield return row;
        }
    }
}
