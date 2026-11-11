namespace ThreeDErp.Domain.Inventory;

/// <summary>
/// 库存移动类型。方向由类型决定；入库可关联供应商（采购收货）或客户（客户退货），
/// 出库可关联客户（销售发货）或供应商（退供应商），调整不带往来单位。
/// </summary>
public enum InventoryMovementType
{
    /// <summary>入库：采购收货、客户退货、盘盈等。</summary>
    In = 1,

    /// <summary>出库：销售发货、领料、退供应商、盘亏等。</summary>
    Out = 2,

    /// <summary>手工调整 / 盘点修正。</summary>
    Adjustment = 3
}
