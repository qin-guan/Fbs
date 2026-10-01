using Fbs.WebApi.Entities;

namespace Fbs.WebApi.Repository;

public interface IUserRepository : IRepository<User>
{
    /// <summary>
    /// Users by phone number, for looking up the users behind many bookings at once.
    /// </summary>
    public Task<Dictionary<string, User>> GetByPhoneAsync(CancellationToken cancellationToken = default);
}
