using ThreeDErp.Domain.Materials;
using ThreeDErp.Domain.Warehouses;

namespace ThreeDErp.Domain.Inventory;

/// <summary>
/// 库存预警规则。每个仓库 × 物料组合仅一条规则（唯一索引约束），
/// 余额低于 MinimumQuantity 且规则启用时触发预警。
/// </summary>
public sealed class InventoryAlertRule
{
    public Guid Id { get; set; }

    /// <summary>仓库 Id，外键指向 Warehouses。</summary>
    public Guid WarehouseId { get; set; }

    /// <summary>物料 Id，外键指向 Materials。</summary>
    public Guid MaterialId { get; set; }

    /// <summary>预警下限，decimal(19,4)。</summary>
    public decimal MinimumQuantity { get; set; }

    /// <summary>是否启用；停用后该规则不参与预警计算。</summary>
    public bool IsEnabled { get; set; } = true;

    public Warehouse Warehouse { get; set; } = null!;

    public Material Material { get; set; } = null!;
}
