using Fbs.WebApi.Data.Entities;

namespace Fbs.WebApi.Tenancy;

/// <summary>
/// Which organisation a request is for, and who in it is making it. Set once, before the endpoint runs, by
/// what resolves <c>/t/{slug}</c>, so endpoints never work out either for themselves, or take one from the
/// request body.
/// </summary>
public interface ITenantContext
{
    Tenant Tenant { get; }

    /// <summary>Who is making the request, which is an active member of <see cref="Tenant"/>.</summary>
    TenantMember Member { get; }

    UserAccount Account { get; }

    bool IsAdmin => Member.Role == MemberRole.Admin;
}

public sealed class TenantContext : ITenantContext
{
    private Tenant? _tenant;
    private TenantMember? _member;
    private UserAccount? _account;

    public Tenant Tenant => _tenant ?? throw NotSet();

    public TenantMember Member => _member ?? throw NotSet();

    public UserAccount Account => _account ?? throw NotSet();

    public void Set(Tenant tenant, TenantMember member, UserAccount account)
    {
        _tenant = tenant;
        _member = member;
        _account = account;
    }

    private static InvalidOperationException NotSet() =>
        new("The organisation is not known yet: this endpoint has to be in the tenant group, which finds it before it runs.");
}
