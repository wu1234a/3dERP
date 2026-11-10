namespace ThreeDErp.Application.Warehouses;

/// <summary>
/// 创建仓库请求。
/// </summary>
public sealed record CreateWarehouseRequest(
    string Code,
    string Name,
    string? Address,
    string? ContactName,
    string? Phone);

/// <summary>
/// 更新仓库请求（全量字段）。
/// </summary>
public sealed record UpdateWarehouseRequest(
    string Code,
    string Name,
    string? Address,
    string? ContactName,
    string? Phone);
