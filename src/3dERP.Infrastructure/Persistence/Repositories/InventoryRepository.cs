using Microsoft.EntityFrameworkCore;
using ThreeDErp.Application.Common;
using ThreeDErp.Application.Inventory;
using ThreeDErp.Domain.Inventory;
using ThreeDErp.Infrastructure.Persistence;

namespace ThreeDErp.Infrastructure.Persistence.Repositories;

/// <summary>
/// 库存仓储的 EF Core 实现。余额查询 AsNoTracking 并 Include 仓库 / 物料主数据，
/// 按 WarehouseId / MaterialId / 物料编码 / 名称过滤，稳定排序保证分页结果确定。
/// </summary>
public sealed class InventoryRepository(ErpDbContext context) : IInventoryRepository
{
    public async Task<PagedResult<InventoryBalance>> FindBalancesAsync(
        int page,
        int pageSize,
        Guid? warehouseId,
        Guid? materialId,
        string? keyword,
        CancellationToken cancellationToken = default)
    {
        IQueryable<InventoryBalance> query = context.InventoryBalances
            .AsNoTracking()
            .Include(balance => balance.Warehouse)
            .Include(balance => balance.Material);

        if (warehouseId is not null)
        {
            query = query.Where(balance => balance.WarehouseId == warehouseId);
        }

        if (materialId is not null)
        {
            query = query.Where(balance => balance.MaterialId == materialId);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(balance =>
                balance.Material.Code.Contains(keyword)
                || balance.Material.Name.Contains(keyword));
        }

        // WarehouseId + MaterialId 唯一，编码排序保证稳定分页。
        query = query
            .OrderBy(balance => balance.Warehouse.Code)
            .ThenBy(balance => balance.Material.Code);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<InventoryBalance>(items, page, pageSize, totalCount);
    }
}
