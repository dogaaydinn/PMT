using System.Linq.Expressions;
using Core.Domain.Abstract;

namespace Core.DataAccess.Abstract;

public interface IEntityRepository<T> where T : class, IEntity, new()
{
    Task<ICollection<T>> GetAllAsync(Expression<Func<T, bool>>? predicate = null,
        Func<IQueryable<T>, IQueryable<T>>? include = null,
        Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null,
        bool enableTracking = false);

    Task<IList<T>> GetAllPaginatedAsync(Expression<Func<T, bool>>? predicate = null,
        Func<IQueryable<T>, IQueryable<T>>? include = null,
        Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null,
        bool enableTracking = false, int page = 1, int pageSize = int.MaxValue);

    Task<T?> GetAsync(Expression<Func<T, bool>> predicate,
        Func<IQueryable<T>, IQueryable<T>>? include = null,
        bool enableTracking = false);

    Task<IQueryable<T>> Find(Expression<Func<T, bool>> predicate, bool enableTracking = false);

    Task<long> CountAsync(Expression<Func<T, bool>>? predicate = null);

    Task<T> AddAsync(T entity);

    Task<IList<T>> AddRangeAsync(IEnumerable<T> entities);

    Task<T> UpdateAsync(T entity);

    Task HardDeleteAsync(T entity);

    Task HardDeleteAsync(string id);

    Task HardDeleteMatchingAsync(IEnumerable<string> ids);

    Task HardDeleteMatchingAsync(params T[] entities);

    Task HardDeleteMatchingAsync(IEnumerable<T> entities);

    Task SoftDeleteAsync(T entity);

    Task SoftDeleteAsync(string id);

    Task SoftDeleteMatchingAsync(IEnumerable<string> ids);

    Task SoftDeleteMatchingAsync(params T[] entities);

    Task SoftDeleteMatchingAsync(IEnumerable<T> entities);

    Task<T?> GetByIdAsync(Guid id);
}