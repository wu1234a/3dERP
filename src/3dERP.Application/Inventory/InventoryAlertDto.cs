using ThreeDErp.Domain.Inventory;

namespace ThreeDErp.Application.Inventory;

/// <summary>
/// 库存预警视图模型：低于预警下限的余额 + 仓库 / 物料摘要与缺口数量。
/// </summary>
public sealed record InventoryAlertDto(
    Guid RuleId,
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    string MaterialUnit,
    decimal OnHandQuantity,
    decimal MinimumQuantity,
    decimal ShortageQuantity)
{
    /// <summary>由启用的预警规则与当前余额构造；无余额记录时现有量按 0 处理。</summary>
    public static InventoryAlertDto From(InventoryAlertRule rule, decimal onHandQuantity) => new(
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
}
