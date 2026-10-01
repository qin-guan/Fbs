using System.Linq.Expressions;
using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Entities;
using SqlSugar;
using User = Fbs.WebApi.Entities.User;

namespace Fbs.WebApi.Repository.Database;

/// <summary>
/// Users from the tenant's members, shaped the way the API has always shown them.
/// </summary>
public class DatabaseUserRepository(ISqlSugarClient sql, DefaultTenant tenant) : IUserRepository
{
    public async Task<List<User>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = await tenant.GetIdAsync(cancellationToken);
        var members = await sql.Queryable<TenantMember>()
            .Where(m => m.TenantId == tenantId && m.Status != MemberStatus.Removed)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);
        var units = await UnitNamesAsync(tenantId, cancellationToken);

        return members.Select(member => ToUser(member, units)).ToList();
    }

    public async Task<Dictionary<string, User>> GetByPhoneAsync(CancellationToken cancellationToken = default)
    {
        var byPhone = new Dictionary<string, User>();
        foreach (var user in await GetListAsync(cancellationToken))
        {
            if (user.Phone is not null)
            {
                byPhone.TryAdd(user.Phone, user);
            }
        }

        return byPhone;
    }

    public async Task<User?> FindAsync(Expression<Func<User, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return (await GetListAsync(cancellationToken)).SingleOrDefault(predicate.Compile());
    }

    public async Task<User> GetAsync(Expression<Func<User, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return (await GetListAsync(cancellationToken)).Single(predicate.Compile());
    }

    public Task<User> InsertAsync(User entity, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    /// <summary>
    /// Saves what the API changes about a user: the Telegram chat, whether they are an admin, and who
    /// they hear about.
    /// </summary>
    public async Task<User> UpdateAsync(User entity, CancellationToken cancellationToken = default)
    {
        var tenantId = await tenant.GetIdAsync(cancellationToken);
        var phone = PhoneNumbers.ToStored(entity.Phone);
        var member = await sql.Queryable<TenantMember>()
            .FirstAsync(m => m.TenantId == tenantId && m.Phone == phone && m.Status != MemberStatus.Removed, cancellationToken);
        if (member is null)
        {
            throw new InvalidOperationException("The user is not a member.");
        }

        member.LegacyChatId = string.IsNullOrWhiteSpace(entity.TelegramChatId) ? null : entity.TelegramChatId;
        member.Role = entity.IsAdmin ? MemberRole.Admin : MemberRole.Member;
        member.NotificationScope = ToScope(entity.NotificationGroup);
        await sql.Updateable(member)
            .UpdateColumns(m => new { m.LegacyChatId, m.Role, m.NotificationScope })
            .ExecuteCommandAsync(cancellationToken);

        return entity;
    }

    public Task DeleteAsync(Expression<Func<User, bool>> predicate, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    private async Task<Dictionary<Guid, string>> UnitNamesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var units = await sql.Queryable<Data.Entities.Unit>().Where(u => u.TenantId == tenantId).ToListAsync(cancellationToken);
        return units.ToDictionary(u => u.Id, u => u.Name);
    }

    private static User ToUser(TenantMember member, Dictionary<Guid, string> units) =>
        new()
        {
            Unit = member.UnitId is { } unitId && units.TryGetValue(unitId, out var unit) ? unit : null,
            Name = member.DisplayName,
            Phone = PhoneNumbers.ToApi(member.Phone),
            TelegramChatId = member.LegacyChatId,
            NotificationGroup = member.NotificationScope.ToString(),
            IsAdmin = member.Role == MemberRole.Admin,
        };

    private static NotificationScope ToScope(string? group) =>
        Enum.TryParse<NotificationScope>(group, ignoreCase: true, out var scope) ? scope : NotificationScope.None;
}
