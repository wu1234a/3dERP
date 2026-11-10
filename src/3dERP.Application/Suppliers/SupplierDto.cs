using ThreeDErp.Domain.Suppliers;

namespace ThreeDErp.Application.Suppliers;

/// <summary>
/// 供应商主数据视图模型。
/// </summary>
public sealed record SupplierDto(
    Guid Id,
    string Code,
    string Name,
    string? ContactName,
    string? Phone,
    string? Email,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static SupplierDto FromEntity(Supplier supplier) => new(
        supplier.Id,
        supplier.Code,
        supplier.Name,
        supplier.ContactName,
        supplier.Phone,
        supplier.Email,
        supplier.IsActive,
        supplier.CreatedAtUtc,
        supplier.UpdatedAtUtc);
}
