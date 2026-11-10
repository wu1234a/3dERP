using ThreeDErp.Domain.Warehouses;

namespace ThreeDErp.Application.Warehouses;

/// <summary>
/// 仓库主数据视图模型。
/// </summary>
public sealed record WarehouseDto(
    Guid Id,
    string Code,
    string Name,
    string? Address,
    string? ContactName,
    string? Phone,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static WarehouseDto FromEntity(Warehouse warehouse) => new(
        warehouse.Id,
        warehouse.Code,
        warehouse.Name,
        warehouse.Address,
        warehouse.ContactName,
        warehouse.Phone,
        warehouse.IsActive,
        warehouse.CreatedAtUtc,
        warehouse.UpdatedAtUtc);
}
