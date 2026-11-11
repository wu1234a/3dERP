using ThreeDErp.Domain.Materials;
using ThreeDErp.Domain.Warehouses;

namespace ThreeDErp.Domain.Inventory;

/// <summary>
/// 库存余额。每个仓库 × 物料组合仅一条记录（唯一索引约束），
/// 余额由出入库流水汇总维护，使用 rowversion 做乐观并发控制。
/// </summary>
public sealed class InventoryBalance
{
    public Guid Id { get; set; }

    /// <summary>仓库 Id，外键指向 Warehouses。</summary>
    public Guid WarehouseId { get; set; }

    /// <summary>物料 Id，外键指向 Materials。</summary>
    public Guid MaterialId { get; set; }

    /// <summary>现有量，decimal(19,4)。</summary>
    public decimal OnHandQuantity { get; set; }

    /// <summary>SQL Server rowversion，乐观并发控制。</summary>
    public byte[] RowVersion { get; set; } = [];

    public Warehouse Warehouse { get; set; } = null!;

    public Material Material { get; set; } = null!;
}
