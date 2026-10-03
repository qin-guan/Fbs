using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;
using TUnit.Assertions.Enums;

namespace Fbs.WebApi.Tests;

/// <summary>What is written down of people: an organisation being made, and them joining, being let in, changed and removed.</summary>
public class AuditPeopleTests
{
    [ClassDataSource<ClerkFbsApiFactory>]
    public required ClerkFbsApiFactory Factory { get; init; }

    private static async Task<List<JsonElement>> AuditOf(HttpClient client, string slug)
    {
        var response = await client.GetAsync($"/t/{slug}/Audit");
        await Assert.That(response).HasStatus(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
    }

    private static string[] Actions(IEnumerable<JsonElement> entries) => entries.Select(e => e.GetProperty("action").GetString()!).ToArray();

    private static string? TargetName(JsonElement entry) => entry.GetProperty("target") is { ValueKind: JsonValueKind.Object } t ? t.GetProperty("displayName").GetString() : null;

    private static string? ActorName(JsonElement entry) => entry.GetProperty("actor") is { ValueKind: JsonValueKind.Object } t ? t.GetProperty("displayName").GetString() : null;

    /// <summary>Changes somebody as an admin does from the page for people: all of them is sent, with what is to change changed.</summary>
    private static async Task<HttpResponseMessage> ChangeAsync(TestOrg org, Guid memberId, Func<Dictionary<string, object?>, Dictionary<string, object?>>? change = null)
    {
        var list = (await org.Admin.GetFromJsonAsync<JsonElement>($"/t/{org.Slug}/Members?includeRemoved=true")).EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == memberId);
        var body = new Dictionary<string, object?>
        {
            ["displayName"] = list.GetProperty("displayName").GetString(),
            ["phone"] = list.GetProperty("phone").GetString() ?? "",
            ["unitId"] = list.GetProperty("unitId").ValueKind == JsonValueKind.Null ? null : list.GetProperty("unitId").GetGuid(),
            ["role"] = list.GetProperty("role").GetString(),
            ["notificationScope"] = list.GetProperty("notificationScope").GetString(),
            ["membership"] = "In",
        };
        return await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Members/{memberId}", change is null ? body : change(body));
    }

    private static async Task<(HttpClient Client, Guid MemberId)> JoinAsync(ClerkFbsApiFactory factory, TestOrg org, string name)
    {
        var token = (await (await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Invites", new { })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        var client = factory.ClientFor(ClerkFbsApiFactory.NewUserId(), name);
        (await client.PostAsJsonAsync($"/Invites/{token}/Accept", new { })).EnsureSuccessStatusCode();
        var accountId = await factory.AccountIdOfAsync(client);
        return (client, factory.Db.Queryable<TenantMember>().First(m => m.TenantId == org.TenantId && m.UserId == accountId).Id);
    }

    [Test]
    public async Task Making_an_organisation_is_written_down_as_done_by_its_founder()
    {
        var org = await Factory.CreateOrgAsync(founderName: "Founder Name");

        var entries = await AuditOf(org.Admin, org.Slug);

        await Assert.That(Actions(entries)).IsEquivalentTo(["tenant.created"], CollectionOrdering.Matching);
        await Assert.That(ActorName(entries[0])).IsEqualTo("Founder Name");
        await Assert.That(entries[0].GetProperty("summary").GetString()).IsEqualTo("Made the organisation.");
    }

    [Test]
    public async Task Somebody_joining_being_let_in_changed_removed_and_let_back_in_are_each_written_down()
    {
        var org = await Factory.CreateOrgAsync(founderName: "Founder Name");
        var unit = org.AddUnit("Alpha");
        var (_, joiner) = await JoinAsync(Factory, org, "Joiner Name");

        (await ChangeAsync(org, joiner)).EnsureSuccessStatusCode();
        (await ChangeAsync(org, joiner, b => { b["role"] = "Admin"; b["unitId"] = unit; return b; })).EnsureSuccessStatusCode();
        (await ChangeAsync(org, joiner, b => { b["displayName"] = "New Name"; b["notificationScope"] = "All"; return b; })).EnsureSuccessStatusCode();
        (await ChangeAsync(org, joiner, b => { b["role"] = "Member"; b["membership"] = "Removed"; return b; })).EnsureSuccessStatusCode();
        (await ChangeAsync(org, joiner)).EnsureSuccessStatusCode();

        var entries = await AuditOf(org.Admin, org.Slug);

        await Assert.That(string.Join(", ", Actions(entries))).IsEqualTo("member.let_back_in, member.removed, member.changed, member.made_admin, member.let_in, member.joined, invite.created, tenant.created");
        var summaries = entries.Select(e => e.GetProperty("summary").GetString()).ToArray();
        await Assert.That(summaries).IsEquivalentTo(
            [
                "Let a person back in.",
                "Removed a person. Made them a member.",
                "Changed their name, whose bookings they are told about.",
                "Made them an admin. Changed their unit.",
                "Let a person in.",
                "Joined with an invite link, and is waiting to be let in.",
                "Made an invite link to join as a member, for 7 days and up to 10 people.",
                "Made the organisation.",
            ],
            CollectionOrdering.Matching
        );

        // Who did it, and to whom: as they are named now, which after being renamed is not what they joined as, and the summaries have no name in them
        await Assert.That(ActorName(entries[5])).IsEqualTo("New Name");
        await Assert.That(TargetName(entries[5])).IsEqualTo("New Name");
        await Assert.That(ActorName(entries[4])).IsEqualTo("Founder Name");
        await Assert.That(TargetName(entries[0])).IsEqualTo("New Name");
        await Assert.That(summaries.Any(s => s!.Contains("Name", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Somebody_waiting_who_is_turned_away_and_somebody_added_by_phone_are_written_down_and_a_change_of_nothing_is_not()
    {
        var org = await Factory.CreateOrgAsync();
        var (_, waiting) = await JoinAsync(Factory, org, "Waiting Person");
        var added = await org.Admin.PostAsJsonAsync($"/t/{org.Slug}/Members", new { displayName = "Added Person", phone = "+6590000001" });
        var addedId = (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var before = (await AuditOf(org.Admin, org.Slug)).Count;

        // Saved as it was: nothing was done
        (await ChangeAsync(org, addedId)).EnsureSuccessStatusCode();
        await Assert.That((await AuditOf(org.Admin, org.Slug)).Count).IsEqualTo(before);

        (await ChangeAsync(org, waiting, b => { b["membership"] = "Removed"; return b; })).EnsureSuccessStatusCode();

        var entries = await AuditOf(org.Admin, org.Slug);
        await Assert.That(entries.Count).IsEqualTo(before + 1);
        await Assert.That(entries[0].GetProperty("action").GetString()).IsEqualTo("member.turned_away");
        await Assert.That(TargetName(entries[0])).IsEqualTo("Waiting Person");
        var addedEntry = entries.Single(e => e.GetProperty("action").GetString() == "member.added");
        await Assert.That(TargetName(addedEntry)).IsEqualTo("Added Person");
        await Assert.That(addedEntry.GetProperty("summary").GetString()).IsEqualTo("Added a person by their phone number.");
    }

    [Test]
    public async Task A_change_that_is_refused_writes_nothing()
    {
        var org = await Factory.CreateOrgAsync();
        var admin = Factory.Db.Queryable<TenantMember>().First(m => m.TenantId == org.TenantId);
        var before = (await AuditOf(org.Admin, org.Slug)).Count;

        // The only admin can't be made a member
        var refused = await ChangeAsync(org, admin.Id, b => { b["role"] = "Member"; return b; });

        await Assert.That(refused).HasStatus(HttpStatusCode.Conflict);
        await Assert.That((await AuditOf(org.Admin, org.Slug)).Count).IsEqualTo(before);
    }
}
