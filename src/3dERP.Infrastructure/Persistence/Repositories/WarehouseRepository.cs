using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ThreeDErp.Application.Common;
using ThreeDErp.Application.Warehouses;
using ThreeDErp.Domain.Warehouses;
using ThreeDErp.Infrastructure.Persistence;

namespace ThreeDErp.Infrastructure.Persistence.Repositories;

/// <summary>
/// 仓库仓储的 EF Core 实现。唯一索引冲突与并发冲突统一翻译为
/// Application 层的 ConflictException。
/// </summary>
public sealed class WarehouseRepository(ErpDbContext context) : IWarehouseRepository
{
    public async Task<PagedResult<Warehouse>> FindAsync(
        int page,
        int pageSize,
        string? keyword,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var query = context.Warehouses.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(warehouse => warehouse.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(warehouse =>
                warehouse.Code.Contains(keyword)
                || warehouse.Name.Contains(keyword)
                || (warehouse.Address != null && warehouse.Address.Contains(keyword)));
        }

        query = query
            .OrderBy(warehouse => warehouse.Code)
            .ThenByDescending(warehouse => warehouse.CreatedAtUtc);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Warehouse>(items, page, pageSize, totalCount);
    }

    public Task<Warehouse?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Warehouses.FirstOrDefaultAsync(warehouse => warehouse.Id == id, cancellationToken);

    public Task<bool> CodeExistsAsync(
        string code,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default) =>
        context.Warehouses
            .AsNoTracking()
            .AnyAsync(warehouse => warehouse.Code == code
                && (excludeId == null || warehouse.Id != excludeId),
                cancellationToken);

    public async Task AddAsync(Warehouse warehouse, CancellationToken cancellationToken = default)
    {
        context.Warehouses.Add(warehouse);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            throw new ConflictException($"仓库编码 {warehouse.Code} 已存在。");
        }
    }

    public async Task UpdateAsync(Warehouse warehouse, CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("仓库已被其他用户修改，请刷新后重试。");
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            throw new ConflictException($"仓库编码 {warehouse.Code} 已存在。");
        }
    }

    /// <summary>SQL Server 唯一索引 / 主键冲突错误号。</summary>
    private static bool IsDuplicateKey(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException
            && sqlException.Number is 2601 or 2627;
}
