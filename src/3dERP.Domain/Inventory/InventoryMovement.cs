using ThreeDErp.Domain.Customers;
using ThreeDErp.Domain.Materials;
using ThreeDErp.Domain.Suppliers;
using ThreeDErp.Domain.Warehouses;

namespace ThreeDErp.Domain.Inventory;

/// <summary>
/// 库存流水（不可变的历史记录，不做物理删除）。余额以流水为准，
/// 同一移动类型下单据号唯一，防止同一单据重复记账。
/// </summary>
public sealed class InventoryMovement
{
    public Guid Id { get; set; }

    /// <summary>移动类型（入库 / 出库 / 调整），决定数量方向。</summary>
    public InventoryMovementType MovementType { get; set; }

    /// <summary>关联业务单据号，如 PO-2026-0001；同类型下唯一，最多 64 位。</summary>
    public string DocumentNo { get; set; } = string.Empty;

    /// <summary>仓库 Id，外键指向 Warehouses。</summary>
    public Guid WarehouseId { get; set; }

    /// <summary>物料 Id，外键指向 Materials。</summary>
    public Guid MaterialId { get; set; }

    /// <summary>移动数量（始终为非负值，方向由 MovementType 决定），decimal(19,4)。</summary>
    public decimal Quantity { get; set; }

    /// <summary>供应商 Id（采购入库 / 退供应商时填写），外键指向 Suppliers。</summary>
    public Guid? SupplierId { get; set; }

    /// <summary>客户 Id（销售出库 / 客户退货时填写），外键指向 Customers。</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>业务发生时间（UTC 存储）。</summary>
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>备注，最多 512 位。</summary>
    public string? Remark { get; set; }

    public Warehouse Warehouse { get; set; } = null!;

    public Material Material { get; set; } = null!;

    public Supplier? Supplier { get; set; }

    public Customer? Customer { get; set; }
}
