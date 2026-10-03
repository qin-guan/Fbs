extern alias Migrator;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fbs.WebApi.Claims;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Tests.Data;
using Fbs.WebApi.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using PromoteAdminCommand = Migrator::Fbs.DbMigrator.Commands.PromoteAdminCommand;

namespace Fbs.WebApi.Tests;

/// <summary>
/// Claiming: a signed-in user attaches their Clerk account to the member imported for them, by opening a one-time Telegram link
/// from the chat the old version sent their login codes to (<see cref="TenantMember.LegacyChatId"/>). See <see cref="MemberClaims"/>.
/// </summary>
public class ClaimsTests
{
    [ClassDataSource<ClerkFbsApiFactory>]
    public required ClerkFbsApiFactory Factory { get; init; }

    /// <summary>
    /// Puts the test organisation in the state <c>import-legacy</c> leaves it in: every member unclaimed, each with a legacy chat,
    /// and claiming on.
    /// </summary>
    private void AsImported(bool claimEnabled = true)
    {
        // Random chat IDs, as the database is shared with other tests
        foreach (var phone in new[] { Users.Booker, Users.SameUnit, Users.AllGroup, Users.OtherUnit, Users.Admin })
        {
            var chat = Random.Shared.NextInt64(1_000_000_000, 9_000_000_000);
            _chats[phone] = chat;
            Factory.Db.Updateable<TenantMember>().SetColumns(m => new TenantMember { LegacyChatId = chat.ToString() }).Where(m => m.Id == Factory.MemberIdOf(phone)).ExecuteCommand();
        }

        Factory.Db.Updateable<TenantMember>().SetColumns(m => new TenantMember { Status = MemberStatus.Unclaimed }).Where(m => m.TenantId == Factory.TenantId).ExecuteCommand();
        Factory.Db.Updateable<Tenant>().SetColumns(t => new Tenant { LegacyClaimEnabled = claimEnabled }).Where(t => t.Id == Factory.TenantId).ExecuteCommand();
    }

    private readonly Dictionary<string, long> _chats = [];

    private long ChatOf(string phone) => _chats[phone];

    private HttpClient Person(string name = "Claimer") => Factory.ClientFor(ClerkFbsApiFactory.NewUserId(), name);

    private static async Task<string> StartAsync(HttpClient client, string slug)
    {
        var response = await client.PostAsync($"/Claims/{slug}/Start", null);
        await Assert.That(response).HasStatus(HttpStatusCode.OK);
        var url = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!;
        return url[(url.IndexOf("start=", StringComparison.Ordinal) + 6)..];
    }

    private Task<HttpResponseMessage> OpenAsync(string startParameter, long chat) => Factory.PostToBotAsync(BotUpdates.Text($"/start {startParameter}", chat));

    private TenantMember Stored(string phone) => Factory.Db.Queryable<TenantMember>().First(m => m.Id == Factory.MemberIdOf(phone));

    private string LastMessageTo(long chat) => Factory.Telegram.Messages.Last(m => m.ChatId == chat).Text;

    [Test]
    public async Task Opening_the_link_from_a_members_legacy_chat_attaches_the_account_to_that_member()
    {
        AsImported();
        var client = Person();
        var accountId = await Factory.AccountIdOfAsync(client);
        var before = Stored(Users.Booker);

        var preview = await client.GetAsync($"/Claims/{Factory.Slug}");
        var parameter = await StartAsync(client, Factory.Slug);
        var opened = await OpenAsync(parameter, chat: ChatOf(Users.Booker));

        await Assert.That(preview).HasStatus(HttpStatusCode.OK);
        await Assert.That((await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("organizationName").GetString()).IsEqualTo("Test");
        await Assert.That(parameter).StartsWith("claim_");
        await Assert.That(opened).HasStatus(HttpStatusCode.OK);
        var after = Stored(Users.Booker);
        await Assert.That(after.UserId).IsEqualTo(accountId);
        await Assert.That(after.Status).IsEqualTo(MemberStatus.Active);
        await Assert.That(after.Role).IsEqualTo(MemberRole.Member);
        // The imported details are kept
        await Assert.That(after.Phone).IsEqualTo(before.Phone);
        await Assert.That(after.DisplayName).IsEqualTo(before.DisplayName);
        await Assert.That(after.UnitId).IsEqualTo(before.UnitId);
        await Assert.That(after.NotificationScope).IsEqualTo(before.NotificationScope);
        await Assert.That(LastMessageTo(ChatOf(Users.Booker))).Contains("Welcome back, CPT Booker");
        // They can use the organisation now, and the chat is connected to their account for notifications
        await Assert.That(await client.GetAsync($"/t/{Factory.Slug}")).HasStatus(HttpStatusCode.OK);
        await Assert.That((await client.GetFromJsonAsync<JsonElement>("/Me/Telegram")).GetProperty("linked").GetBoolean()).IsTrue();
        await Assert.That(Factory.Db.Queryable<TelegramLink>().First(l => l.UserId == accountId)!.ChatId).IsEqualTo(ChatOf(Users.Booker).ToString());
        await Assert.That(Factory.Db.Queryable<MemberClaimToken>().First(t => t.UserId == accountId)!.UsedAt).IsNotNull();
    }

    [Test]
    public async Task A_legacy_admin_who_claims_becomes_a_member()
    {
        AsImported();
        var client = Person("Old admin");
        var parameter = await StartAsync(client, Factory.Slug);
        Factory.Db.Updateable<TenantMember>().SetColumns(m => new TenantMember { Role = MemberRole.Admin }).Where(m => m.Id == Factory.MemberIdOf(Users.Admin)).ExecuteCommand();

        (await OpenAsync(parameter, chat: ChatOf(Users.Admin))).EnsureSuccessStatusCode();

        await Assert.That(Stored(Users.Admin).Role).IsEqualTo(MemberRole.Member);
        await Assert.That(await client.GetAsync($"/t/{Factory.Slug}")).HasStatus(HttpStatusCode.OK);
        await Assert.That(await client.GetAsync($"/t/{Factory.Slug}/Settings")).HasStatus(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task A_chat_with_no_imported_member_claims_nothing_and_the_link_still_works_from_the_right_chat()
    {
        AsImported();
        var client = Person();
        var parameter = await StartAsync(client, Factory.Slug);

        (await OpenAsync(parameter, chat: 999999)).EnsureSuccessStatusCode();

        await Assert.That(LastMessageTo(999999)).Contains("not linked to anyone in Test");
        await Assert.That(Factory.Db.Queryable<TenantMember>().Count(m => m.TenantId == Factory.TenantId && m.UserId != null)).IsEqualTo(0);
        (await OpenAsync(parameter, chat: ChatOf(Users.Booker))).EnsureSuccessStatusCode();
        await Assert.That(Stored(Users.Booker).Status).IsEqualTo(MemberStatus.Active);
    }

    [Test]
    public async Task A_link_works_once_and_a_new_one_replaces_it()
    {
        AsImported();
        var client = Person();
        var old = await StartAsync(client, Factory.Slug);
        var replacement = await StartAsync(client, Factory.Slug);

        (await OpenAsync(old, chat: ChatOf(Users.Booker))).EnsureSuccessStatusCode();
        await Assert.That(LastMessageTo(ChatOf(Users.Booker))).Contains("expired or has been used");
        await Assert.That(Stored(Users.Booker).Status).IsEqualTo(MemberStatus.Unclaimed);

        (await OpenAsync(replacement, chat: ChatOf(Users.Booker))).EnsureSuccessStatusCode();
        await Assert.That(Stored(Users.Booker).Status).IsEqualTo(MemberStatus.Active);
        // Once used, opening it again does nothing
        (await OpenAsync(replacement, chat: ChatOf(Users.Booker))).EnsureSuccessStatusCode();
        await Assert.That(LastMessageTo(ChatOf(Users.Booker))).Contains("expired or has been used");
        await Assert.That(Factory.Db.Queryable<TenantMember>().Count(m => m.TenantId == Factory.TenantId && m.UserId != null)).IsEqualTo(1);
    }

    [Test]
    public async Task An_expired_link_claims_nothing()
    {
        AsImported();
        var client = Person();
        var accountId = await Factory.AccountIdOfAsync(client);
        var parameter = await StartAsync(client, Factory.Slug);
        Factory.Db.Updateable<MemberClaimToken>().SetColumns(t => new MemberClaimToken { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-5) }).Where(t => t.UserId == accountId).ExecuteCommand();

        (await OpenAsync(parameter, chat: ChatOf(Users.Booker))).EnsureSuccessStatusCode();

        await Assert.That(LastMessageTo(ChatOf(Users.Booker))).Contains("expired or has been used");
        await Assert.That(Stored(Users.Booker).Status).IsEqualTo(MemberStatus.Unclaimed);
    }

    [Test]
    public async Task Only_a_private_chat_with_a_token_that_was_issued_can_claim()
    {
        AsImported();
        var client = Person();
        var parameter = await StartAsync(client, Factory.Slug);

        (await Factory.PostToBotAsync(BotUpdates.Text($"/start {parameter}", ChatOf(Users.Booker), "group"))).EnsureSuccessStatusCode();
        (await OpenAsync($"claim_{new string('a', 43)}", chat: ChatOf(Users.Booker))).EnsureSuccessStatusCode();

        await Assert.That(Stored(Users.Booker).Status).IsEqualTo(MemberStatus.Unclaimed);
        await Assert.That(await Factory.PostToBotAsync(BotUpdates.Text($"/start {parameter}", ChatOf(Users.Booker)), secret: null)).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(Stored(Users.Booker).Status).IsEqualTo(MemberStatus.Unclaimed);
    }

    [Test]
    public async Task When_claiming_is_off_the_endpoints_are_404_and_links_made_earlier_do_nothing()
    {
        AsImported(claimEnabled: false);
        var client = Person();

        await Assert.That(await client.GetAsync($"/Claims/{Factory.Slug}")).HasStatus(HttpStatusCode.NotFound);
        await Assert.That(await client.PostAsync($"/Claims/{Factory.Slug}/Start", null)).HasStatus(HttpStatusCode.NotFound);
        await Assert.That(await client.GetAsync("/Claims/no-such-organization")).HasStatus(HttpStatusCode.NotFound);

        Factory.Db.Updateable<Tenant>().SetColumns(t => new Tenant { LegacyClaimEnabled = true }).Where(t => t.Id == Factory.TenantId).ExecuteCommand();
        var parameter = await StartAsync(client, Factory.Slug);
        Factory.Db.Updateable<Tenant>().SetColumns(t => new Tenant { LegacyClaimEnabled = false }).Where(t => t.Id == Factory.TenantId).ExecuteCommand();
        (await OpenAsync(parameter, chat: ChatOf(Users.Booker))).EnsureSuccessStatusCode();

        await Assert.That(LastMessageTo(ChatOf(Users.Booker))).Contains("not available");
        await Assert.That(Stored(Users.Booker).Status).IsEqualTo(MemberStatus.Unclaimed);
    }

    [Test]
    public async Task An_existing_member_cannot_claim_and_claiming_needs_sign_in()
    {
        AsImported();
        var client = Person();
        var accountId = await Factory.AccountIdOfAsync(client);
        Factory.Db.Updateable<TenantMember>().SetColumns(m => new TenantMember { UserId = accountId, Status = MemberStatus.Active }).Where(m => m.Id == Factory.MemberIdOf(Users.SameUnit)).ExecuteCommand();
        using var nobody = Factory.CreateClient();

        await Assert.That(await client.GetAsync($"/Claims/{Factory.Slug}")).HasStatus(HttpStatusCode.NotFound);
        await Assert.That(await client.PostAsync($"/Claims/{Factory.Slug}/Start", null)).HasStatus(HttpStatusCode.NotFound);
        await Assert.That(await nobody.GetAsync($"/Claims/{Factory.Slug}")).HasStatus(HttpStatusCode.Unauthorized);
        await Assert.That(await nobody.PostAsync($"/Claims/{Factory.Slug}/Start", null)).HasStatus(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Only_unclaimed_members_can_be_claimed_and_a_chat_shared_by_two_members_claims_neither()
    {
        AsImported();
        var client = Person();
        var parameter = await StartAsync(client, Factory.Slug);
        // Booker is already active rather than unclaimed, and AllGroup now has the same legacy chat as SameUnit
        Factory.Db.Updateable<TenantMember>().SetColumns(m => new TenantMember { Status = MemberStatus.Active }).Where(m => m.Id == Factory.MemberIdOf(Users.Booker)).ExecuteCommand();
        var shared = ChatOf(Users.SameUnit);
        Factory.Db.Updateable<TenantMember>().SetColumns(m => new TenantMember { LegacyChatId = shared.ToString() }).Where(m => m.Id == Factory.MemberIdOf(Users.AllGroup)).ExecuteCommand();

        (await OpenAsync(parameter, chat: ChatOf(Users.Booker))).EnsureSuccessStatusCode();
        await Assert.That(LastMessageTo(ChatOf(Users.Booker))).Contains("not linked to anyone");
        (await OpenAsync(parameter, chat: shared)).EnsureSuccessStatusCode();
        await Assert.That(LastMessageTo(shared)).Contains("not linked to anyone");

        await Assert.That(Factory.Db.Queryable<TenantMember>().Count(m => m.TenantId == Factory.TenantId && m.UserId != null)).IsEqualTo(0);
    }

    [Test]
    public async Task Links_of_several_accounts_opened_from_one_chat_at_once_claim_the_member_once()
    {
        AsImported();
        var people = Enumerable.Range(0, 5).Select(i => Person($"Claimer {i}")).ToList();
        var parameters = new List<string>();
        foreach (var person in people)
        {
            parameters.Add(await StartAsync(person, Factory.Slug));
        }

        List<Task<HttpResponseMessage>> requests;
        using (ExecutionContext.SuppressFlow())
        {
            requests = parameters.Select(p => Task.Run(() => OpenAsync(p, chat: ChatOf(Users.Booker)))).ToList();
        }

        (await Task.WhenAll(requests)).ToList().ForEach(r => r.EnsureSuccessStatusCode());

        await Assert.That(Factory.Db.Queryable<TenantMember>().Count(m => m.TenantId == Factory.TenantId && m.UserId != null)).IsEqualTo(1);
        await Assert.That(Factory.Db.Queryable<MemberClaimToken>().Count(t => t.TenantId == Factory.TenantId && t.UsedAt != null)).IsEqualTo(1);
        await Assert.That(Factory.Telegram.Messages.Count(m => m.ChatId == ChatOf(Users.Booker) && m.Text.StartsWith("Welcome back"))).IsEqualTo(1);
    }

    [Test]
    public async Task An_organisation_can_turn_claiming_off_but_not_back_on()
    {
        var org = await Factory.CreateOrgAsync();
        Factory.Db.Updateable<Tenant>().SetColumns(t => new Tenant { LegacyClaimEnabled = true }).Where(t => t.Id == org.TenantId).ExecuteCommand();
        object Settings(bool? claim) => new { name = "Test Org", timeZone = "Asia/Singapore", defaultCountryCode = "65", slotMinutes = 30, requireApproval = true, legacyClaimEnabled = claim };

        var kept = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Settings", Settings(null));
        var off = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Settings", Settings(false));
        var on = await org.Admin.PutAsJsonAsync($"/t/{org.Slug}/Settings", Settings(true));

        await Assert.That((await kept.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("legacyClaimEnabled").GetBoolean()).IsTrue();
        await Assert.That((await off.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("legacyClaimEnabled").GetBoolean()).IsFalse();
        await Assert.That(on).HasStatus(HttpStatusCode.BadRequest);
        await Assert.That(await on.Content.ReadAsStringAsync()).Contains("claim-cannot-enable");
        await Assert.That(Factory.Db.Queryable<Tenant>().First(t => t.Id == org.TenantId).LegacyClaimEnabled).IsFalse();
        // An organisation made in the app, not imported, can't turn it on either
        var plain = await Factory.CreateOrgAsync();
        await Assert.That(await plain.Admin.PutAsJsonAsync($"/t/{plain.Slug}/Settings", Settings(true))).HasStatus(HttpStatusCode.BadRequest);
        var read = await plain.Admin.GetFromJsonAsync<JsonElement>($"/t/{plain.Slug}/Settings");
        await Assert.That(read.GetProperty("legacyClaimEnabled").GetBoolean()).IsFalse();
    }

    [Test]
    public async Task Promote_admin_finds_the_member_by_phone_and_only_promotes_active_members()
    {
        AsImported();
        var promotions = new MemberPromotions(Factory.Db);
        var claimer = Person();
        (await OpenAsync(await StartAsync(claimer, Factory.Slug), chat: ChatOf(Users.Booker))).EnsureSuccessStatusCode();
        // Booker has claimed and has the number +6591234567, which a member of another organisation has too
        var other = await Factory.CreateOrgAsync();
        Factory.Db.Insertable(new TenantMember { Id = Guid.NewGuid(), TenantId = other.TenantId, DisplayName = "Same number", Phone = "+6591234567", Status = MemberStatus.Active }).ExecuteCommand();
        Factory.Db.Updateable<TenantMember>().SetColumns(m => new TenantMember { Phone = "+6591234567" }).Where(m => m.Id == Factory.MemberIdOf(Users.Booker)).ExecuteCommand();

        var unclaimed = await promotions.PromoteAsync(Factory.Slug, Users.SameUnit, default);
        var missing = await promotions.PromoteAsync(Factory.Slug, "+6500000000", default);
        var noOrg = await promotions.PromoteAsync("no-such-org", "+6591234567", default);
        // A local number is read in the organisation's country
        var promoted = await promotions.PromoteAsync(Factory.Slug, "9123 4567", default);
        var again = await promotions.PromoteAsync(Factory.Slug, "+65 9123 4567", default);

        await Assert.That(unclaimed).IsEqualTo(PromotionOutcome.NotActive);
        await Assert.That(missing).IsEqualTo(PromotionOutcome.NoSuchMember);
        await Assert.That(noOrg).IsEqualTo(PromotionOutcome.NoSuchOrganization);
        await Assert.That(promoted).IsEqualTo(PromotionOutcome.Promoted);
        await Assert.That(again).IsEqualTo(PromotionOutcome.AlreadyAdmin);
        await Assert.That(Factory.Db.Queryable<TenantMember>().First(m => m.TenantId == Factory.TenantId && m.Phone == "+6591234567").Role).IsEqualTo(MemberRole.Admin);
        await Assert.That(Factory.Db.Queryable<TenantMember>().First(m => m.TenantId == other.TenantId && m.Phone == "+6591234567").Role).IsEqualTo(MemberRole.Member);
        await Assert.That(await claimer.GetAsync($"/t/{Factory.Slug}/Settings")).HasStatus(HttpStatusCode.OK);
    }

    [Test]
    public async Task Promote_admin_exits_with_1_when_it_cannot_promote()
    {
        AsImported();
        var command = new PromoteAdminCommand(NullLogger<PromoteAdminCommand>.Instance, new MemberPromotions(Factory.Db));
        var claimer = Person();
        (await OpenAsync(await StartAsync(claimer, Factory.Slug), chat: ChatOf(Users.Booker))).EnsureSuccessStatusCode();

        await Assert.That(await command.Promote(Factory.Slug, Users.Booker)).IsEqualTo(0);
        await Assert.That(await command.Promote(Factory.Slug, Users.Booker)).IsEqualTo(0);
        await Assert.That(await command.Promote(Factory.Slug, Users.SameUnit)).IsEqualTo(1);
        await Assert.That(await command.Promote(Factory.Slug, "+6500000000")).IsEqualTo(1);
        await Assert.That(await command.Promote("no-such-org", Users.Booker)).IsEqualTo(1);
        await Assert.That(Factory.Db.Queryable<TenantMember>().First(m => m.Id == Factory.MemberIdOf(Users.Booker)).Role).IsEqualTo(MemberRole.Admin);
    }
}
