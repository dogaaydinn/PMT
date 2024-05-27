using System.Linq.Expressions;
using Core.Context.EntityFramework;
using Core.DataAccess.Abstract;
using Core.Domain.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Core.DataAccess.EntityFramework;

public class EfEntityRepository<TEntity, TContext> : IEntityRepository<TEntity>
    where TEntity : class, IEntity, new()
    where TContext : EfDbContextBase, new()
{
    public async Task<IList<TEntity>> GetAllAsync(Expression<Func<TEntity, bool>>? predicate = null,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? include = null,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null, bool enableTracking = false)
    {
        await using var context = new TContext();
        IQueryable<TEntity> query = context.Set<TEntity>();

        if (predicate != null) query = query.Where(predicate);

        if (include != null) query = include(query);

        if (orderBy != null) query = orderBy(query);
        
        return await query.ToListAsync();
    }

    public async Task<IList<TEntity>> GetAllPaginatedAsync(Expression<Func<TEntity, bool>>? predicate = null,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? include = null,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
        bool enableTracking = false, int currentPage = 1, int pageSize = int.MaxValue)
    {
        await using var context = new TContext();
        IQueryable<TEntity> query = context.Set<TEntity>();

        if (predicate != null) query = query.Where(predicate);

        if (include != null) query = include(query);

        if (orderBy != null) query = orderBy(query);

        return await query.Skip((currentPage - 1) * pageSize).Take(pageSize).ToListAsync();
    }

    public async Task<TEntity?> GetAsync(Expression<Func<TEntity, bool>> predicate,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? include = null, bool enableTracking = false)
    {
        await using var context = new TContext();
        IQueryable<TEntity> query = context.Set<TEntity>();

        if (!enableTracking)
            query = query.AsNoTracking();

        if (include != null)
            query = include(query);

        return await query.FirstOrDefaultAsync(predicate);
    }

    public Task<IQueryable<TEntity>> Find(Expression<Func<TEntity, bool>> predicate, bool enableTracking = false)
    {
        using var context = new TContext();
        IQueryable<TEntity> query = context.Set<TEntity>();

        if (!enableTracking)
            query = query.AsNoTracking();

        return Task.FromResult(query.Where(predicate));
    }

    public async Task<long> CountAsync(Expression<Func<TEntity, bool>>? predicate = null)
    {
        await using var context = new TContext();
        IQueryable<TEntity> query = context.Set<TEntity>();

        if (predicate != null) query = query.Where(predicate);

        return await query.LongCountAsync();
    }

    public async Task<TEntity> AddAsync(TEntity entity)
    {
        await using var context = new TContext();
        await context.AddAsync(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    public async Task<IList<TEntity>> AddRangeAsync(IEnumerable<TEntity> entities)
    {
        var enumerable = entities as TEntity[] ?? entities.ToArray();
        await using var context = new TContext();
        await context.AddRangeAsync(enumerable.ToList());
        await context.SaveChangesAsync();
        return enumerable.ToList();
    }

    public async Task<TEntity> UpdateAsync(TEntity entity)
    {
        await using var context = new TContext();
        context.Update(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    public async Task HardDeleteAsync(TEntity entity)
    {
        await using var context = new TContext();
        context.Remove(entity);
        await context.SaveChangesAsync();
    }

    public async Task HardDeleteAsync(string id)
    {
        await using var context = new TContext();
        var entity = await context.Set<TEntity>().FindAsync(id);
        if (entity != null)
            context.Remove(entity);

        await context.SaveChangesAsync();
    }

    public async Task HardDeleteMatchingAsync(IEnumerable<string> ids)
    {
        await using var context = new TContext();
        foreach (var id in ids)
        {
            var entity = await context.Set<TEntity>().FindAsync(id);
            if (entity != null)
                context.Remove(entity);
        }

        await context.SaveChangesAsync();
    }

    public async Task HardDeleteMatchingAsync(params TEntity[] entities)
    {
        await using var context = new TContext();
        context.RemoveRange(entities.ToList());
        await context.SaveChangesAsync();
    }

    public async Task HardDeleteMatchingAsync(IEnumerable<TEntity> entities)
    {
        await using var context = new TContext();
        context.RemoveRange(entities);
        await context.SaveChangesAsync();
    }

    public async Task SoftDeleteAsync(TEntity entity)
    {
        (entity as EntityBase)!.IsDeleted = true;
        await using var context = new TContext();
        context.Update(entity);
        await context.SaveChangesAsync();
    }

    public async Task SoftDeleteAsync(string id)
    {
        await using var context = new TContext();
        var entity = await context.Set<TEntity>().FindAsync(id);
        if (entity == null)
            return;

        (entity as EntityBase)!.IsDeleted = true;
        context.Update(entity);
        await context.SaveChangesAsync();
    }

    public async Task SoftDeleteMatchingAsync(IEnumerable<string> ids)
    {
        await using var context = new TContext();
        foreach (var id in ids)
        {
            var entity = await context.Set<TEntity>().FindAsync(id);
            if (entity == null)
                continue;

            (entity as EntityBase)!.IsDeleted = true;
            context.Update(entity);
        }

        await context.SaveChangesAsync();
    }

    public async Task SoftDeleteMatchingAsync(params TEntity[] entities)
    {
        await using var context = new TContext();
        foreach (var entity in entities)
            (entity as EntityBase)!.IsDeleted = true;
        context.UpdateRange(entities.ToList());
        await context.SaveChangesAsync();
    }

    public async Task SoftDeleteMatchingAsync(IEnumerable<TEntity> entities)
    {
        await using var context = new TContext();
        var enumerable = entities as TEntity[] ?? entities.ToArray();
        foreach (var entity in enumerable)
            (entity as EntityBase)!.IsDeleted = true;
        context.UpdateRange(enumerable.ToList());
        await context.SaveChangesAsync();
    }
}