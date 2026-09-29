using System.Linq.Expressions;
using Fbs.WebApi.Data;
using Fbs.WebApi.Data.Entities;
using Fbs.WebApi.Entities;
using SqlSugar;
using Otp = Fbs.WebApi.Entities.Otp;

namespace Fbs.WebApi.Repository.Database;

public class DatabaseOtpRepository(ISqlSugarClient sql) : IOtpRepository
{
    public async Task<List<Otp>> GetListAsync(CancellationToken cancellationToken = default) =>
        (await sql.Queryable<LoginOtp>().ToListAsync(cancellationToken)).Select(ToOtp).ToList();

    public async Task<Otp?> FindAsync(Expression<Func<Otp, bool>> predicate, CancellationToken cancellationToken = default) =>
        (await GetListAsync(cancellationToken)).SingleOrDefault(predicate.Compile());

    public async Task<Otp> GetAsync(Expression<Func<Otp, bool>> predicate, CancellationToken cancellationToken = default) =>
        (await GetListAsync(cancellationToken)).Single(predicate.Compile());

    public async Task<Otp> InsertAsync(Otp entity, CancellationToken cancellationToken = default)
    {
        await sql.Insertable(
                new LoginOtp
                {
                    Id = Guid.NewGuid(),
                    Phone = PhoneNumbers.ToStored(entity.Phone)!,
                    CodeHash = entity.Code!,
                    CreatedAt = entity.CreatedAt ?? DateTimeOffset.UtcNow,
                }
            )
            .ExecuteCommandAsync(cancellationToken);

        return entity;
    }

    public async Task<Otp> UpdateAsync(Otp entity, CancellationToken cancellationToken = default)
    {
        var phone = PhoneNumbers.ToStored(entity.Phone)!;
        var code = entity.Code!;
        var createdAt = entity.CreatedAt ?? DateTimeOffset.UtcNow;
        await sql.Updateable<LoginOtp>()
            .SetColumns(o => new LoginOtp { CodeHash = code, CreatedAt = createdAt })
            .Where(o => o.Phone == phone)
            .ExecuteCommandAsync(cancellationToken);

        return entity;
    }

    public async Task DeleteAsync(Expression<Func<Otp, bool>> predicate, CancellationToken cancellationToken = default)
    {
        foreach (var otp in (await GetListAsync(cancellationToken)).Where(predicate.Compile()))
        {
            var phone = PhoneNumbers.ToStored(otp.Phone)!;
            await sql.Deleteable<LoginOtp>().Where(o => o.Phone == phone).ExecuteCommandAsync(cancellationToken);
        }
    }

    private static Otp ToOtp(LoginOtp otp) =>
        new() { Phone = PhoneNumbers.ToApi(otp.Phone), Code = otp.CodeHash, CreatedAt = otp.CreatedAt };
}
