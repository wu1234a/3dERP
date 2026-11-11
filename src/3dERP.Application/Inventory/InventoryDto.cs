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
