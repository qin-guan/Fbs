using System.Linq.Expressions;

namespace Fbs.WebApi.Repository;

public interface IRepository<TEntity> where TEntity : Entity
{
    public Task<List<TEntity>> GetListAsync(
        CancellationToken cancellationToken = default
    );

    public Task<TEntity?> FindAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default
    );

    public Task<TEntity> GetAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default
    );
    
    public Task<TEntity> InsertAsync(
        TEntity entity,
        CancellationToken cancellationToken = default
    );

    public Task<TEntity> UpdateAsync(
        TEntity entity,
        CancellationToken cancellationToken = default
    );

    public Task DeleteAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default
    );
}