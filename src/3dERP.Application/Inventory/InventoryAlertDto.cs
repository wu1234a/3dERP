namespace ThreeDErp.Application.Inventory;

/// <summary>
/// 预警类型常量。现有 InventoryAlertRule 模型仅支持"下限阈值"（MinimumQuantity），
/// 因此当前只有"余额低于下限"一种预警类型；不虚构模型不支持的预警状态。
/// </summary>
public static class InventoryAlertTypes
{
    /// <summary>余额低于启用规则的下限（现有模型唯一支持的预警类型）。</summary>
    public const string LowStock = "lowStock";
}

/// <summary>
/// 实时库存预警视图模型：库存维度（仓库 × 物料）、当前数量、命中的规则、
/// 阈值与预警类型。结果按当前余额 + 启用规则实时计算，不落库。
/// </summary>
public sealed record InventoryAlertDto(
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    string MaterialUnit,
    decimal OnHandQuantity,
    Guid RuleId,
    decimal MinimumQuantity,
    string AlertType);
