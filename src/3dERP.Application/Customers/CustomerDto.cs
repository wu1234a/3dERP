using ThreeDErp.Domain.Customers;

namespace ThreeDErp.Application.Customers;

/// <summary>
/// 客户主数据视图模型。
/// </summary>
public sealed record CustomerDto(
    Guid Id,
    string Code,
    string Name,
    string? ContactName,
    string? Phone,
    string? Email,
    string? Address,
    string? TaxNo,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static CustomerDto FromEntity(Customer customer) => new(
        customer.Id,
        customer.Code,
        customer.Name,
        customer.ContactName,
        customer.Phone,
        customer.Email,
        customer.Address,
        customer.TaxNo,
        customer.IsActive,
        customer.CreatedAtUtc,
        customer.UpdatedAtUtc);
}
