using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;
using AuditEntry = Fbs.WebApi.Data.Entities.AuditEntry;

namespace Fbs.WebApi.Tests;

/// <summary>
/// What somebody is called in an organisation, which starts as the name on their account and follows it, until it is made something
/// else there.
/// </summary>
public class MemberNamesTests
{
    [ClassDataSource<ClerkFbsApiFactory>]
    public required ClerkFbsApiFactory Factory { get; init; }

    private string NameIn(Guid tenantId, Guid accountId) => Factory.Db.Queryable<TenantMember>().First(m => m.TenantId == tenantId && m.UserId == accountId).DisplayName;

    private int Renames(Guid tenantId) => Factory.Db.Queryable<AuditEntry>().Count(e => e.TenantId == tenantId && e.Action == "member.renamed");

    /// <summary>An organisation that the person signed in with the client makes, so they are called by the name on their account in it.</summary>
    private async Task<Guid> MakeOrgAsync(HttpClient client)
    {
        var slug = ClerkFbsApiFactory.NewSlug();
        (await client.PostAsJsonAsync("/Tenants", new { name = "Their Org", slug, timeZone = "Asia/Singapore" })).EnsureSuccessStatusCode();
        return Factory.Db.Queryable<Tenant>().First(t => t.Slug == slug).Id;
    }

    [Test]
    public async Task Changing_the_name_with_clerk_changes_it_where_it_was_the_same_and_nowhere_else()
    {
        var userId = ClerkFbsApiFactory.NewUserId();
        var before = Factory.ClientFor(userId, "Tan Ah Kow");
        var accountId = await Factory.AccountIdOfAsync(before);
        var same = await MakeOrgAsync(before);
        // Somewhere they are called something else, somewhere they are waiting to be let in, and somewhere they have left
        var different = (await Factory.CreateOrgAsync()).TenantId;
        var waiting = (await Factory.CreateOrgAsync()).TenantId;
        var left = (await Factory.CreateOrgAsync()).TenantId;
        Factory.Db.Insertable(
                new List<TenantMember>
                {
                    new TenantMember { Id = Guid.NewGuid(), TenantId = different, UserId = accountId, DisplayName = "CPT Tan", Status = MemberStatus.Active },
                    new TenantMember { Id = Guid.NewGuid(), TenantId = waiting, UserId = accountId, DisplayName = "Tan Ah Kow", Status = MemberStatus.Pending },
                    new TenantMember { Id = Guid.NewGuid(), TenantId = left, UserId = accountId, DisplayName = "Tan Ah Kow", Status = MemberStatus.Removed },
                }
            )
            .ExecuteCommand();

        var after = Factory.ClientFor(userId, "Ah Kow Tan");
        var me = await after.GetFromJsonAsync<JsonElement>("/Me");
        // Asked again, there is nothing more to do
        await after.GetFromJsonAsync<JsonElement>("/Me");

        await Assert.That(me.GetProperty("name").GetString()).IsEqualTo("Ah Kow Tan");
        await Assert.That(NameIn(same, accountId)).IsEqualTo("Ah Kow Tan");
        await Assert.That(NameIn(waiting, accountId)).IsEqualTo("Ah Kow Tan");
        await Assert.That(NameIn(different, accountId)).IsEqualTo("CPT Tan");
        await Assert.That(NameIn(left, accountId)).IsEqualTo("Tan Ah Kow");
        await Assert.That(me.GetProperty("memberships").EnumerateArray().Select(m => m.GetProperty("displayName").GetString())).IsEquivalentTo(["Ah Kow Tan", "Ah Kow Tan", "CPT Tan"]);
        // The organisations it changed in say they did it
        await Assert.That(Renames(same)).IsEqualTo(1);
        await Assert.That(Renames(waiting)).IsEqualTo(1);
        await Assert.That(Renames(different)).IsEqualTo(0);
        await Assert.That(Renames(left)).IsEqualTo(0);
    }

    [Test]
    public async Task A_member_changes_what_they_are_called_and_by_the_name_on_their_account_it_follows_that_again()
    {
        var userId = ClerkFbsApiFactory.NewUserId();
        var client = Factory.ClientFor(userId, "Tan Ah Kow");
        var accountId = await Factory.AccountIdOfAsync(client);
        var tenantId = await MakeOrgAsync(client);
        var slug = Factory.Db.Queryable<Tenant>().First(t => t.Id == tenantId).Slug;

        var response = await client.PutAsJsonAsync($"/t/{slug}/Me", new { displayName = " CPT Tan " });

        await Assert.That(response).HasStatus(HttpStatusCode.OK);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(me.GetProperty("displayName").GetString()).IsEqualTo("CPT Tan");
        await Assert.That(me.GetProperty("role").GetString()).IsEqualTo("Admin");
        await Assert.That((await client.GetFromJsonAsync<JsonElement>($"/t/{slug}")).GetProperty("me").GetProperty("displayName").GetString()).IsEqualTo("CPT Tan");
        var entry = Factory.Db.Queryable<AuditEntry>().First(e => e.TenantId == tenantId && e.Action == "member.renamed");
        await Assert.That(entry.ActorMemberId).IsEqualTo(me.GetProperty("memberId").GetGuid());
        await Assert.That(entry.TargetId).IsEqualTo(me.GetProperty("memberId").GetGuid());

        // Called something else, it stays when the name on the account changes
        var renamed = Factory.ClientFor(userId, "Ah Kow Tan");
        await renamed.GetFromJsonAsync<JsonElement>("/Me");
        await Assert.That(NameIn(tenantId, accountId)).IsEqualTo("CPT Tan");

        // And back to the name on the account, it follows it again
        (await renamed.PutAsJsonAsync($"/t/{slug}/Me", new { displayName = "Ah Kow Tan" })).EnsureSuccessStatusCode();
        await Factory.ClientFor(userId, "Tan").GetFromJsonAsync<JsonElement>("/Me");
        await Assert.That(NameIn(tenantId, accountId)).IsEqualTo("Tan");
    }

    [Test]
    public async Task No_name_too_long_a_one_or_somebody_who_is_not_in_yet_is_refused()
    {
        var org = await Factory.CreateOrgAsync();
        var (member, memberId) = await org.AddMemberAsync("A Member");
        var (waiting, _) = await org.AddMemberAsync("Waiting", status: MemberStatus.Pending);
        var outsider = Factory.ClientFor(ClerkFbsApiFactory.NewUserId());

        await Assert.That(await member.PutAsJsonAsync($"/t/{org.Slug}/Me", new { displayName = "  " })).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await member.PutAsJsonAsync($"/t/{org.Slug}/Me", new { displayName = new string('a', 201) })).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await waiting.PutAsJsonAsync($"/t/{org.Slug}/Me", new { displayName = "Let me in" })).HasStatus(HttpStatusCode.Forbidden);
        await Assert.That(await outsider.PutAsJsonAsync($"/t/{org.Slug}/Me", new { displayName = "Not one of them" })).HasStatus(HttpStatusCode.NotFound);
        await Assert.That(Factory.Db.Queryable<TenantMember>().First(m => m.Id == memberId).DisplayName).IsEqualTo("A Member");
        await Assert.That(Renames(org.TenantId)).IsEqualTo(0);
    }
}
