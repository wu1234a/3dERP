using ThreeDErp.Domain.Inventory;

namespace ThreeDErp.Application.Inventory;

/// <summary>
/// 库存预警规则视图模型：仅映射现有 InventoryAlertRule 支持的字段——
/// 适用范围（仓库 × 物料）、阈值（预警下限 MinimumQuantity）与启停（IsEnabled）。
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
    public static InventoryAlertRuleDto FromEntity(InventoryAlertRule rule) => new(
        rule.Id,
        rule.WarehouseId,
        rule.Warehouse.Code,
        rule.Warehouse.Name,
        rule.MaterialId,
        rule.Material.Code,
        rule.Material.Name,
        rule.Material.Unit,
        rule.MinimumQuantity,
        rule.IsEnabled);
}
