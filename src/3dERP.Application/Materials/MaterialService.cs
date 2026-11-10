using ThreeDErp.Application.Common;
using ThreeDErp.Domain.Materials;

namespace ThreeDErp.Application.Materials;

/// <summary>
/// 物料主数据查询 / 写入服务。校验、去重、时间戳等业务逻辑集中在这里，
/// 不放在 API endpoint 中。
/// </summary>
public sealed class MaterialService(
    IMaterialRepository repository,
    MaterialRequestValidator validator)
{
    private const int DefaultPageSize = 10;
    private const int MaxPageSize = 100;

    public async Task<PagedResult<MaterialDto>> SearchAsync(
        int page,
        int pageSize,
        string? keyword,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var normalizedKeyword = MaterialRequestValidator.Normalize(keyword);
        var result = await repository.FindAsync(
            page,
            pageSize,
            normalizedKeyword.Length == 0 ? null : normalizedKeyword,
            includeInactive,
            cancellationToken);

        return new PagedResult<MaterialDto>(
            [.. result.Items.Select(MaterialDto.FromEntity)],
            result.Page,
            result.PageSize,
            result.TotalCount);
    }

    public async Task<MaterialDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var material = await repository.FindByIdAsync(id, cancellationToken);
        return material is null ? null : MaterialDto.FromEntity(material);
    }

    public async Task<MaterialDto> CreateAsync(
        CreateMaterialRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = validator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var code = MaterialRequestValidator.Normalize(request.Code);
        if (await repository.CodeExistsAsync(code, null, cancellationToken))
        {
            throw new ConflictException($"物料编码 {code} 已存在。");
        }

        var now = DateTime.UtcNow;
        var material = new Material
        {
            Id = Guid.CreateVersion7(),
            Code = code,
            Name = MaterialRequestValidator.Normalize(request.Name),
            Specification = Optional(request.Specification),
            Unit = MaterialRequestValidator.Normalize(request.Unit),
            MaterialType = MaterialRequestValidator.Normalize(request.MaterialType),
            UnitPrice = request.UnitPrice,
            SafetyStock = request.SafetyStock,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await repository.AddAsync(material, cancellationToken);
        return MaterialDto.FromEntity(material);
    }

    public async Task<MaterialDto> UpdateAsync(
        Guid id,
        UpdateMaterialRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = validator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var material = await repository.FindByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("物料不存在或已被删除。");

        var code = MaterialRequestValidator.Normalize(request.Code);
        if (!string.Equals(material.Code, code, StringComparison.Ordinal)
            && await repository.CodeExistsAsync(code, material.Id, cancellationToken))
        {
            throw new ConflictException($"物料编码 {code} 已存在。");
        }

        material.Code = code;
        material.Name = MaterialRequestValidator.Normalize(request.Name);
        material.Specification = Optional(request.Specification);
        material.Unit = MaterialRequestValidator.Normalize(request.Unit);
        material.MaterialType = MaterialRequestValidator.Normalize(request.MaterialType);
        material.UnitPrice = request.UnitPrice;
        material.SafetyStock = request.SafetyStock;
        material.UpdatedAtUtc = DateTime.UtcNow;

        await repository.UpdateAsync(material, cancellationToken);
        return MaterialDto.FromEntity(material);
    }

    /// <summary>
    /// 停用物料（软删除），不做物理删除。重复停用保持幂等。
    /// </summary>
    public async Task<MaterialDto> DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var material = await repository.FindByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("物料不存在或已被删除。");

        if (material.IsActive)
        {
            material.IsActive = false;
            material.UpdatedAtUtc = DateTime.UtcNow;
            await repository.UpdateAsync(material, cancellationToken);
        }

        return MaterialDto.FromEntity(material);
    }

    private static string? Optional(string? value)
    {
        var normalized = MaterialRequestValidator.Normalize(value);
        return normalized.Length == 0 ? null : normalized;
    }
}
