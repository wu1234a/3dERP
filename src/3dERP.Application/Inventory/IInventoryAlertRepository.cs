using ThreeDErp.Application.Common;
using ThreeDErp.Domain.Inventory;

namespace ThreeDErp.Application.Inventory;

/// <summary>
/// 实时预警查询行：余额 × 启用规则的匹配结果。仓储层已按
/// "规则启用 + 当前余额低于规则下限"过滤，并带出主数据摘要。
/// </summary>
public sealed record InventoryAlertRow(
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    string MaterialUnit,
    decimal OnHandQuantity,
    Guid RuleId,
    decimal MinimumQuantity);

/// <summary>
/// 库存预警仓储端口。Application 层定义契约，Infrastructure 层提供 EF Core 实现。
/// 复用现有 InventoryAlertRule / InventoryBalance 表，不新增预警存储。
/// </summary>
public interface IInventoryAlertRepository
{
    /// <summary>
    /// 分页查询预警规则，可按仓库 / 物料 / 启停过滤，keyword 匹配物料编码 / 名称。
    /// 返回的规则已加载 Warehouse / Material 导航属性，供服务层映射主数据摘要。
    /// </summary>
    Task<PagedResult<InventoryAlertRule>> FindRulesAsync(
        int page,
        int pageSize,
        Guid? warehouseId,
        Guid? materialId,
        bool? isEnabled,
        string? keyword,
        CancellationToken cancellationToken = default);

    /// <summary>按 Id 查询规则（含主数据导航属性，跟踪状态，供更新使用）。</summary>
    Task<InventoryAlertRule?> FindRuleByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>判断仓库 × 物料组合是否已存在规则，可排除指定规则（更新场景）。</summary>
    Task<bool> RuleExistsAsync(
        Guid warehouseId,
        Guid materialId,
        Guid? excludeRuleId = null,
        CancellationToken cancellationToken = default);

    Task<bool> WarehouseExistsAsync(Guid warehouseId, CancellationToken cancellationToken = default);

    Task<bool> MaterialExistsAsync(Guid materialId, CancellationToken cancellationToken = default);

    Task AddRuleAsync(InventoryAlertRule rule, CancellationToken cancellationToken = default);

    Task UpdateRuleAsync(InventoryAlertRule rule, CancellationToken cancellationToken = default);

    /// <summary>
    /// 实时计算预警：当前余额 × 启用规则，余额低于规则下限时命中。
    /// 可按仓库 / 物料过滤，keyword 匹配物料编码 / 名称。
    /// </summary>
    Task<PagedResult<InventoryAlertRow>> FindAlertsAsync(
        int page,
        int pageSize,
        Guid? warehouseId,
        Guid? materialId,
        string? keyword,
        CancellationToken cancellationToken = default);
}
