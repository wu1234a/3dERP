using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ThreeDErp.Application.Common;
using ThreeDErp.Application.Inventory;
using ThreeDErp.Domain.Inventory;
using ThreeDErp.Infrastructure.Persistence;

namespace ThreeDErp.Infrastructure.Persistence.Repositories;

/// <summary>
/// 库存仓储的 EF Core 实现。余额 / 规则查询 AsNoTracking 并 Include 仓库 / 物料主数据，
/// 稳定排序保证分页结果确定；唯一索引冲突与并发冲突统一翻译为 Application 层的
/// ConflictException。预警查询在数据库端完成规则 × 余额左联接与过滤（无余额记录视为 0）。
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

    public async Task<PagedResult<InventoryAlertRule>> FindAlertRulesAsync(
        int page,
        int pageSize,
        Guid? warehouseId,
        Guid? materialId,
        bool includeDisabled,
        CancellationToken cancellationToken = default)
    {
        IQueryable<InventoryAlertRule> query = context.InventoryAlertRules
            .AsNoTracking()
            .Include(rule => rule.Warehouse)
            .Include(rule => rule.Material);

        if (!includeDisabled)
        {
            query = query.Where(rule => rule.IsEnabled);
        }

        if (warehouseId is not null)
        {
            query = query.Where(rule => rule.WarehouseId == warehouseId);
        }

        if (materialId is not null)
        {
            query = query.Where(rule => rule.MaterialId == materialId);
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

    public Task<InventoryAlertRule?> FindAlertRuleByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        context.InventoryAlertRules
            .Include(rule => rule.Warehouse)
            .Include(rule => rule.Material)
            .FirstOrDefaultAsync(rule => rule.Id == id, cancellationToken);

    public Task<bool> AlertRuleExistsAsync(
        Guid warehouseId,
        Guid materialId,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default) =>
        context.InventoryAlertRules
            .AsNoTracking()
            .AnyAsync(rule => rule.WarehouseId == warehouseId
                && rule.MaterialId == materialId
                && (excludeId == null || rule.Id != excludeId),
                cancellationToken);

    public async Task AddAlertRuleAsync(
        InventoryAlertRule rule,
        CancellationToken cancellationToken = default)
    {
        context.InventoryAlertRules.Add(rule);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            throw new ConflictException("该仓库与物料的预警规则已存在。");
        }
    }

    public async Task UpdateAlertRuleAsync(
        InventoryAlertRule rule,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("预警规则已被其他用户修改，请刷新后重试。");
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            throw new ConflictException("该仓库与物料的预警规则已存在。");
        }
    }

    public async Task DeleteAlertRuleAsync(
        InventoryAlertRule rule,
        CancellationToken cancellationToken = default)
    {
        context.InventoryAlertRules.Remove(rule);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("预警规则已被其他用户修改，请刷新后重试。");
        }
    }

    public async Task<PagedResult<InventoryAlertDto>> FindAlertsAsync(
        int page,
        int pageSize,
        Guid? warehouseId,
        Guid? materialId,
        CancellationToken cancellationToken = default)
    {
        IQueryable<InventoryAlertRule> rules = context.InventoryAlertRules
            .AsNoTracking()
            .Where(rule => rule.IsEnabled);

        if (warehouseId is not null)
        {
            rules = rules.Where(rule => rule.WarehouseId == warehouseId);
        }

        if (materialId is not null)
        {
            rules = rules.Where(rule => rule.MaterialId == materialId);
        }

        // 启用规则左联接余额：无余额记录视为现有量 0（仍低于正数下限时同样触发预警）。
        var query =
            from rule in rules
            join balance in context.InventoryBalances.AsNoTracking()
                on new { rule.WarehouseId, rule.MaterialId }
                equals new { balance.WarehouseId, balance.MaterialId } into grouped
            from balance in grouped.DefaultIfEmpty()
            let onHandQuantity = balance == null ? 0m : balance.OnHandQuantity
            where onHandQuantity < rule.MinimumQuantity
            orderby rule.Warehouse.Code, rule.Material.Code
            select new InventoryAlertDto(
                rule.Id,
                rule.WarehouseId,
                rule.Warehouse.Code,
                rule.Warehouse.Name,
                rule.MaterialId,
                rule.Material.Code,
                rule.Material.Name,
                rule.Material.Unit,
                onHandQuantity,
                rule.MinimumQuantity,
                rule.MinimumQuantity - onHandQuantity);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<InventoryAlertDto>(items, page, pageSize, totalCount);
    }

    /// <summary>SQL Server 唯一索引 / 主键冲突错误号。</summary>
    private static bool IsDuplicateKey(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException
            && sqlException.Number is 2601 or 2627;
}
