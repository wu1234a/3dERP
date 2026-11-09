namespace ThreeDErp.Application.Customers;

/// <summary>
/// 创建客户请求。
/// </summary>
public sealed record CreateCustomerRequest(
    string Code,
    string Name,
    string? ContactName,
    string? Phone,
    string? Email,
    string? Address,
    string? TaxNo);

/// <summary>
/// 更新客户请求（全量字段）。
/// </summary>
public sealed record UpdateCustomerRequest(
    string Code,
    string Name,
    string? ContactName,
    string? Phone,
    string? Email,
    string? Address,
    string? TaxNo);
