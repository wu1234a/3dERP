using ThreeDErp.Domain.Materials;

namespace ThreeDErp.Application.Materials;

/// <summary>
/// 物料主数据视图模型。
/// </summary>
public sealed record MaterialDto(
    Guid Id,
    string Code,
    string Name,
    string? Specification,
    string Unit,
    string MaterialType,
    decimal UnitPrice,
    decimal SafetyStock,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static MaterialDto FromEntity(Material material) => new(
        material.Id,
        material.Code,
        material.Name,
        material.Specification,
        material.Unit,
        material.MaterialType,
        material.UnitPrice,
        material.SafetyStock,
        material.IsActive,
        material.CreatedAtUtc,
        material.UpdatedAtUtc);
}
