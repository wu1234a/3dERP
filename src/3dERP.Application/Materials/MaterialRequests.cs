namespace ThreeDErp.Application.Materials;

/// <summary>
/// 创建物料请求。
/// </summary>
public sealed record CreateMaterialRequest(
    string Code,
    string Name,
    string? Specification,
    string Unit,
    string MaterialType,
    decimal UnitPrice,
    decimal SafetyStock);

/// <summary>
/// 更新物料请求（全量字段）。
/// </summary>
public sealed record UpdateMaterialRequest(
    string Code,
    string Name,
    string? Specification,
    string Unit,
    string MaterialType,
    decimal UnitPrice,
    decimal SafetyStock);
