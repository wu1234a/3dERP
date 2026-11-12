using ThreeDErp.Domain.Inventory;

namespace ThreeDErp.Application.Inventory;

/// <summary>
/// 库存余额视图模型：仓库 / 物料主数据摘要 + 当前数量。
/// </summary>
public sealed record InventoryBalanceDto(
    Guid Id,
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    string MaterialUnit,
    decimal OnHandQuantity)
{
    public static InventoryBalanceDto FromEntity(InventoryBalance balance) => new(
        balance.Id,
        balance.WarehouseId,
        balance.Warehouse.Code,
        balance.Warehouse.Name,
        balance.MaterialId,
        balance.Material.Code,
        balance.Material.Name,
        balance.Material.Unit,
        balance.OnHandQuantity);
}

/// <summary>
/// 入库 / 出库事务结果视图模型：写入的流水 + 事务提交后的库存余额。
/// </summary>
public sealed record InventoryMovementDto(
    Guid Id,
    string MovementType,
    string DocumentNo,
    Guid WarehouseId,
    Guid MaterialId,
    decimal Quantity,
    Guid? SupplierId,
    Guid? CustomerId,
    DateTime OccurredAtUtc,
    string? Note,
    decimal OnHandQuantity)
{
    public static InventoryMovementDto FromEntity(InventoryMovement movement, InventoryBalance balance) => new(
        movement.Id,
        movement.MovementType.ToString(),
        movement.DocumentNo,
        movement.WarehouseId,
        movement.MaterialId,
        movement.Quantity,
        movement.SupplierId,
        movement.CustomerId,
        movement.OccurredAtUtc,
        movement.Remark,
        balance.OnHandQuantity);
}
