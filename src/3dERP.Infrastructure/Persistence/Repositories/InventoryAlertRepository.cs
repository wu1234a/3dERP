using Microsoft.EntityFrameworkCore;
using ThreeDErp.Application.Common;
using ThreeDErp.Application.Inventory;
using ThreeDErp.Domain.Inventory;
using ThreeDErp.Infrastructure.Persistence;

namespace ThreeDErp.Infrastructure.Persistence.Repositories;

/// <summary>
/// 库存预警仓储的 EF Core 实现。规则查询 AsNoTracking 并 Include 仓库 / 物料主数据；
/// 实时预警由 InventoryAlertRules 与 InventoryBalances 按 WarehouseId + MaterialId
/// 在数据库端联接，仅取启用规则且余额低于下限的组合，稳定排序保证分页确定。
/// </summary>
public sealed class InventoryAlertRepository(ErpDbContext context) : IInventoryAlertRepository
{
    public async Task<PagedResult<InventoryAlertRule>> FindRulesAsync(
        int page,
        int pageSize,
        Guid? warehouseId,
        Guid? materialId,
        bool? isEnabled,
        string? keyword,
        CancellationToken cancellationToken = default)
    {
        IQueryable<InventoryAlertRule> query = context.InventoryAlertRules
            .AsNoTracking()
            .Include(rule => rule.Warehouse)
            .Include(rule => rule.Material);

        if (warehouseId is not null)
        {
            query = query.Where(rule => rule.WarehouseId == warehouseId);
        }

        if (materialId is not null)
        {
            query = query.Where(rule => rule.MaterialId == materialId);
        }

        if (isEnabled is not null)
        {
            query = query.Where(rule => rule.IsEnabled == isEnabled);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(rule =>
                rule.Material.Code.Contains(keyword)
                || rule.Material.Name.Contains(keyword));
        }

        // WarehouseId + MaterialId 唯一，编码排序保证稳定分页。
        query = query
            .OrderBy(rule => rule.Warehouse.Code)
            .ThenBy(rule => rule.Material.Code);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<InventoryAlertRule>(items, page, pageSize, totalCount);
    }

    public Task<InventoryAlertRule?> FindRuleByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.InventoryAlertRules
            .Include(rule => rule.Warehouse)
            .Include(rule => rule.Material)
            .FirstOrDefaultAsync(rule => rule.Id == id, cancellationToken);

    public Task<bool> RuleExistsAsync(
        Guid warehouseId,
        Guid materialId,
        Guid? excludeRuleId = null,
        CancellationToken cancellationToken = default) =>
        context.InventoryAlertRules.AnyAsync(
            rule => rule.WarehouseId == warehouseId
                && rule.MaterialId == materialId
                && (excludeRuleId == null || rule.Id != excludeRuleId),
            cancellationToken);

    public Task<bool> WarehouseExistsAsync(Guid warehouseId, CancellationToken cancellationToken = default) =>
        context.Warehouses.AnyAsync(warehouse => warehouse.Id == warehouseId, cancellationToken);

    public Task<bool> MaterialExistsAsync(Guid materialId, CancellationToken cancellationToken = default) =>
        context.Materials.AnyAsync(material => material.Id == materialId, cancellationToken);

    public async Task AddRuleAsync(InventoryAlertRule rule, CancellationToken cancellationToken = default)
    {
        context.InventoryAlertRules.Add(rule);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateRuleAsync(InventoryAlertRule rule, CancellationToken cancellationToken = default)
    {
        context.InventoryAlertRules.Update(rule);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<InventoryAlertRow>> FindAlertsAsync(
        int page,
        int pageSize,
        Guid? warehouseId,
        Guid? materialId,
        string? keyword,
        CancellationToken cancellationToken = default)
    {
        // 实时预警：启用规则联接当前余额，余额低于规则下限即命中。
        // 不读取流水，规则或余额变化后再次查询即反映最新结果。
        var query =
            from rule in context.InventoryAlertRules.AsNoTracking()
            join balance in context.InventoryBalances.AsNoTracking()
                on new { rule.WarehouseId, rule.MaterialId }
                equals new { balance.WarehouseId, balance.MaterialId }
            where rule.IsEnabled && balance.OnHandQuantity < rule.MinimumQuantity
            select new InventoryAlertRow(
                balance.WarehouseId,
                rule.Warehouse.Code,
                rule.Warehouse.Name,
                balance.MaterialId,
                rule.Material.Code,
                rule.Material.Name,
                rule.Material.Unit,
                balance.OnHandQuantity,
                rule.Id,
                rule.MinimumQuantity);

        if (warehouseId is not null)
        {
            query = query.Where(row => row.WarehouseId == warehouseId);
        }

        if (materialId is not null)
        {
            query = query.Where(row => row.MaterialId == materialId);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(row =>
                row.MaterialCode.Contains(keyword)
                || row.MaterialName.Contains(keyword));
        }

        // 与余额 / 规则查询一致的稳定排序：仓库编码升序，再按物料编码升序。
        query = query
            .OrderBy(row => row.WarehouseCode)
            .ThenBy(row => row.MaterialCode);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<InventoryAlertRow>(items, page, pageSize, totalCount);
    }
}
