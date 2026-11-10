namespace ThreeDErp.Application.Suppliers;

/// <summary>
/// 创建供应商请求。
/// </summary>
public sealed record CreateSupplierRequest(
    string Code,
    string Name,
    string? ContactName,
    string? Phone,
    string? Email);

/// <summary>
/// 更新供应商请求（全量字段）。
/// </summary>
public sealed record UpdateSupplierRequest(
    string Code,
    string Name,
    string? ContactName,
    string? Phone,
    string? Email);
