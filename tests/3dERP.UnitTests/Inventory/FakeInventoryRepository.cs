using ThreeDErp.Application.Common;
using ThreeDErp.Application.Inventory;
using ThreeDErp.Domain.Inventory;

namespace ThreeDErp.UnitTests.Inventory;

/// <summary>
/// IInventoryRepository 的内存假实现，供库存服务 / 预警服务单元测试共用。
/// 过滤、排序、分页与预警计算（无余额记录视为 0）语义与 EF 实现保持一致。
/// </summary>
public sealed class FakeInventoryRepository : IInventoryRepository
{
    public List<InventoryBalance> Balances { get; } = [];

    public List<InventoryAlertRule> AlertRules { get; } = [];

    public int? LastPage { get; private set; }

    public int? LastPageSize { get; private set; }

    public Guid? LastWarehouseId { get; private set; }

    public Guid? LastMaterialId { get; private set; }

    public string? LastKeyword { get; private set; }

    public bool? LastIncludeDisabled { get; private set; }

    public Task<PagedResult<InventoryBalance>> FindBalancesAsync(
        int page,
        int pageSize,
        Guid? warehouseId,
        Guid? materialId,
        string? keyword,
        CancellationToken cancellationToken = default)
    {
        LastPage = page;
        LastPageSize = pageSize;
        LastWarehouseId = warehouseId;
        LastMaterialId = materialId;
        LastKeyword = keyword;

        IEnumerable<InventoryBalance> query = Balances;

        if (warehouseId is not null)
        {
            query = query.Where(balance => balance.WarehouseId == warehouseId);
        }

        if (materialId is not null)
        {
            query = query.Where(balance => balance.MaterialId == materialId);
        }

        if (keyword is not null)
        {
            query = query.Where(balance =>
                balance.Material.Code.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || balance.Material.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = query
            .OrderBy(balance => balance.Warehouse.Code)
            .ThenBy(balance => balance.Material.Code)
            .ToList();
        var items = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Task.FromResult(new PagedResult<InventoryBalance>(items, page, pageSize, ordered.Count));
    }

    public Task<PagedResult<InventoryAlertRule>> FindAlertRulesAsync(
        int page,
        int pageSize,
        Guid? warehouseId,
        Guid? materialId,
        bool includeDisabled,
        CancellationToken cancellationToken = default)
    {
        LastPage = page;
        LastPageSize = pageSize;
        LastWarehouseId = warehouseId;
        LastMaterialId = materialId;
        LastIncludeDisabled = includeDisabled;

        IEnumerable<InventoryAlertRule> query = AlertRules;

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

        var ordered = query
            .OrderBy(rule => rule.Warehouse.Code)
            .ThenBy(rule => rule.Material.Code)
            .ToList();
        var items = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Task.FromResult(new PagedResult<InventoryAlertRule>(items, page, pageSize, ordered.Count));
    }

    public Task<InventoryAlertRule?> FindAlertRuleByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(AlertRules.FirstOrDefault(rule => rule.Id == id));

    public Task<bool> AlertRuleExistsAsync(
        Guid warehouseId,
        Guid materialId,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(AlertRules.Any(rule =>
            rule.WarehouseId == warehouseId
            && rule.MaterialId == materialId
            && (excludeId is null || rule.Id != excludeId)));

    public Task AddAlertRuleAsync(
        InventoryAlertRule rule,
        CancellationToken cancellationToken = default)
    {
        AlertRules.Add(rule);
        return Task.CompletedTask;
    }

    public Task UpdateAlertRuleAsync(
        InventoryAlertRule rule,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task DeleteAlertRuleAsync(
        InventoryAlertRule rule,
        CancellationToken cancellationToken = default)
    {
        AlertRules.Remove(rule);
        return Task.CompletedTask;
    }

    public Task<PagedResult<InventoryAlertDto>> FindAlertsAsync(
        int page,
        int pageSize,
        Guid? warehouseId,
        Guid? materialId,
        CancellationToken cancellationToken = default)
    {
        LastPage = page;
        LastPageSize = pageSize;
        LastWarehouseId = warehouseId;
        LastMaterialId = materialId;

        var onHandByPair = Balances.ToDictionary(
            balance => (balance.WarehouseId, balance.MaterialId),
            balance => balance.OnHandQuantity);

        IEnumerable<InventoryAlertRule> query = AlertRules
            .Where(rule => rule.IsEnabled)
            .OrderBy(rule => rule.Warehouse.Code)
            .ThenBy(rule => rule.Material.Code);

        if (warehouseId is not null)
        {
            query = query.Where(rule => rule.WarehouseId == warehouseId);
        }

        if (materialId is not null)
        {
            query = query.Where(rule => rule.MaterialId == materialId);
        }

        var alerts = query
            .Select(rule => (
                Rule: rule,
                OnHandQuantity: onHandByPair.TryGetValue(
                    (rule.WarehouseId, rule.MaterialId),
                    out var onHand) ? onHand : 0m))
            .Where(row => row.OnHandQuantity < row.Rule.MinimumQuantity)
            .ToList();

        var items = alerts
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => InventoryAlertDto.From(row.Rule, row.OnHandQuantity))
            .ToList();

        return Task.FromResult(new PagedResult<InventoryAlertDto>(items, page, pageSize, alerts.Count));
    }

    public Task<InventoryBalance?> FindBalanceAsync(
        Guid warehouseId,
        Guid materialId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<InventoryBalance?>(null);

    public Task<bool> DocumentNoExistsAsync(
        InventoryMovementType movementType,
        string documentNo,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<bool> WarehouseExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<bool> MaterialExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<bool> SupplierExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<bool> CustomerExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<InventoryBalance> RecordMovementAsync(
        InventoryMovement movement,
        InventoryBalance? balance,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("查询和预警测试不涉及库存事务写入。");
}
