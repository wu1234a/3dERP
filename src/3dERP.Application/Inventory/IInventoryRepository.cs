using ThreeDErp.Application.Common;
using ThreeDErp.Domain.Inventory;

namespace ThreeDErp.Application.Inventory;

/// <summary>
/// 库存仓储端口。Application 层定义契约，Infrastructure 层提供 EF Core 实现。
/// 查询返回的余额 / 规则已加载 Warehouse / Material 导航属性，供服务层映射主数据摘要；
/// 预警查询直接返回投影后的 InventoryAlertDto（规则 × 余额跨实体读模型）。
/// </summary>
public interface IInventoryRepository
{
    /// <summary>
    /// 分页查询库存余额，可按仓库、物料过滤，keyword 匹配物料编码 / 名称。
    /// </summary>
    Task<PagedResult<InventoryBalance>> FindBalancesAsync(
        int page,
        int pageSize,
        Guid? warehouseId,
        Guid? materialId,
        string? keyword,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 分页查询预警规则，可按仓库 / 物料过滤；includeDisabled 为 false 时仅返回启用中的规则。
    /// </summary>
    Task<PagedResult<InventoryAlertRule>> FindAlertRulesAsync(
        int page,
        int pageSize,
        Guid? warehouseId,
        Guid? materialId,
        bool includeDisabled,
        CancellationToken cancellationToken = default);

    /// <summary>按 Id 查询单条预警规则（含仓库 / 物料导航）。</summary>
    Task<InventoryAlertRule?> FindAlertRuleByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>检查某仓库 × 物料组合是否已存在预警规则，excludeId 用于更新时排除自身。</summary>
    Task<bool> AlertRuleExistsAsync(
        Guid warehouseId,
        Guid materialId,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default);

    Task AddAlertRuleAsync(InventoryAlertRule rule, CancellationToken cancellationToken = default);

    Task UpdateAlertRuleAsync(InventoryAlertRule rule, CancellationToken cancellationToken = default);

    Task DeleteAlertRuleAsync(InventoryAlertRule rule, CancellationToken cancellationToken = default);

    /// <summary>
    /// 分页查询库存预警：启用中的规则关联当前余额，返回余额低于 MinimumQuantity
    /// （无余额记录视为 0）的组合，含仓库 / 物料摘要与缺口数量。
    /// </summary>
    Task<PagedResult<InventoryAlertDto>> FindAlertsAsync(
        int page,
        int pageSize,
        Guid? warehouseId,
        Guid? materialId,
        CancellationToken cancellationToken = default);
}
