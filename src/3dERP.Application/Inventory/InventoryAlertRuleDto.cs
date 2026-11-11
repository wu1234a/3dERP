using ThreeDErp.Domain.Inventory;
using ThreeDErp.Domain.Materials;
using ThreeDErp.Domain.Warehouses;

namespace ThreeDErp.Application.Inventory;

/// <summary>
/// 库存预警规则视图模型：仓库 / 物料主数据摘要 + 预警下限与启用状态。
/// </summary>
public sealed record InventoryAlertRuleDto(
    Guid Id,
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    string MaterialUnit,
    decimal MinimumQuantity,
    bool IsEnabled)
{
    /// <summary>
    /// 由规则实体与仓库 / 物料主数据构造。创建 / 更新后导航属性可能尚未加载，
    /// 因此主数据摘要由服务层显式传入。
    /// </summary>
    public static InventoryAlertRuleDto From(
        InventoryAlertRule rule,
        Warehouse warehouse,
        Material material) => new(
        rule.Id,
        rule.WarehouseId,
        warehouse.Code,
        warehouse.Name,
        rule.MaterialId,
        material.Code,
        material.Name,
        material.Unit,
        rule.MinimumQuantity,
        rule.IsEnabled);

    /// <summary>由已加载 Warehouse / Material 导航属性的规则实体构造。</summary>
    public static InventoryAlertRuleDto FromEntity(InventoryAlertRule rule) =>
        From(rule, rule.Warehouse, rule.Material);
}
