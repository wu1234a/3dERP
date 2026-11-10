using ThreeDErp.Application.Common;
using ThreeDErp.Domain.Warehouses;

namespace ThreeDErp.Application.Warehouses;

/// <summary>
/// 仓库主数据查询 / 写入服务。校验、去重、时间戳等业务逻辑集中在这里，
/// 不放在 API endpoint 中。
/// </summary>
public sealed class WarehouseService(
    IWarehouseRepository repository,
    WarehouseRequestValidator validator)
{
    private const int DefaultPageSize = 10;
    private const int MaxPageSize = 100;

    public async Task<PagedResult<WarehouseDto>> SearchAsync(
        int page,
        int pageSize,
        string? keyword,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var normalizedKeyword = WarehouseRequestValidator.Normalize(keyword);
        var result = await repository.FindAsync(
            page,
            pageSize,
            normalizedKeyword.Length == 0 ? null : normalizedKeyword,
            includeInactive,
            cancellationToken);

        return new PagedResult<WarehouseDto>(
            [.. result.Items.Select(WarehouseDto.FromEntity)],
            result.Page,
            result.PageSize,
            result.TotalCount);
    }

    public async Task<WarehouseDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var warehouse = await repository.FindByIdAsync(id, cancellationToken);
        return warehouse is null ? null : WarehouseDto.FromEntity(warehouse);
    }

    public async Task<WarehouseDto> CreateAsync(
        CreateWarehouseRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = validator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var code = WarehouseRequestValidator.Normalize(request.Code);
        if (await repository.CodeExistsAsync(code, null, cancellationToken))
        {
            throw new ConflictException($"仓库编码 {code} 已存在。");
        }

        var now = DateTime.UtcNow;
        var warehouse = new Warehouse
        {
            Id = Guid.CreateVersion7(),
            Code = code,
            Name = WarehouseRequestValidator.Normalize(request.Name),
            Address = Optional(request.Address),
            ContactName = Optional(request.ContactName),
            Phone = Optional(request.Phone),
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await repository.AddAsync(warehouse, cancellationToken);
        return WarehouseDto.FromEntity(warehouse);
    }

    public async Task<WarehouseDto> UpdateAsync(
        Guid id,
        UpdateWarehouseRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = validator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var warehouse = await repository.FindByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("仓库不存在或已被删除。");

        var code = WarehouseRequestValidator.Normalize(request.Code);
        if (!string.Equals(warehouse.Code, code, StringComparison.Ordinal)
            && await repository.CodeExistsAsync(code, warehouse.Id, cancellationToken))
        {
            throw new ConflictException($"仓库编码 {code} 已存在。");
        }

        warehouse.Code = code;
        warehouse.Name = WarehouseRequestValidator.Normalize(request.Name);
        warehouse.Address = Optional(request.Address);
        warehouse.ContactName = Optional(request.ContactName);
        warehouse.Phone = Optional(request.Phone);
        warehouse.UpdatedAtUtc = DateTime.UtcNow;

        await repository.UpdateAsync(warehouse, cancellationToken);
        return WarehouseDto.FromEntity(warehouse);
    }

    /// <summary>
    /// 停用仓库（软删除），不做物理删除。重复停用保持幂等。
    /// </summary>
    public async Task<WarehouseDto> DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var warehouse = await repository.FindByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("仓库不存在或已被删除。");

        if (warehouse.IsActive)
        {
            warehouse.IsActive = false;
            warehouse.UpdatedAtUtc = DateTime.UtcNow;
            await repository.UpdateAsync(warehouse, cancellationToken);
        }

        return WarehouseDto.FromEntity(warehouse);
    }

    private static string? Optional(string? value)
    {
        var normalized = WarehouseRequestValidator.Normalize(value);
        return normalized.Length == 0 ? null : normalized;
    }
}
