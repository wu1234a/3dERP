namespace ThreeDErp.Application.Inventory;

/// <summary>
/// 库存入库 / 出库事务请求。入库时可填 supplierId（采购收货 / 客户退货），
/// 出库时可填 customerId（销售发货 / 退供应商），note 为业务备注。
/// </summary>
public sealed record InventoryTransactionRequest(
    Guid WarehouseId,
    Guid MaterialId,
    decimal Quantity,
    string DocumentNo,
    Guid? SupplierId,
    Guid? CustomerId,
    string? Note);
