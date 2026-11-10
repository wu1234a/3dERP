using ThreeDErp.Application.Common;
using ThreeDErp.Domain.Suppliers;

namespace ThreeDErp.Application.Suppliers;

/// <summary>
/// 供应商主数据查询 / 写入服务。校验、去重、时间戳等业务逻辑集中在这里，
/// 不放在 API endpoint 中。
/// </summary>
public sealed class SupplierService(
    ISupplierRepository repository,
    SupplierRequestValidator validator)
{
    private const int DefaultPageSize = 10;
    private const int MaxPageSize = 100;

    public async Task<PagedResult<SupplierDto>> SearchAsync(
        int page,
        int pageSize,
        string? keyword,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var normalizedKeyword = SupplierRequestValidator.Normalize(keyword);
        var result = await repository.FindAsync(
            page,
            pageSize,
            normalizedKeyword.Length == 0 ? null : normalizedKeyword,
            includeInactive,
            cancellationToken);

        return new PagedResult<SupplierDto>(
            [.. result.Items.Select(SupplierDto.FromEntity)],
            result.Page,
            result.PageSize,
            result.TotalCount);
    }

    public async Task<SupplierDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var supplier = await repository.FindByIdAsync(id, cancellationToken);
        return supplier is null ? null : SupplierDto.FromEntity(supplier);
    }

    public async Task<SupplierDto> CreateAsync(
        CreateSupplierRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = validator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var code = SupplierRequestValidator.Normalize(request.Code);
        if (await repository.CodeExistsAsync(code, null, cancellationToken))
        {
            throw new ConflictException($"供应商编码 {code} 已存在。");
        }

        var now = DateTime.UtcNow;
        var supplier = new Supplier
        {
            Id = Guid.CreateVersion7(),
            Code = code,
            Name = SupplierRequestValidator.Normalize(request.Name),
            ContactName = Optional(request.ContactName),
            Phone = Optional(request.Phone),
            Email = Optional(request.Email),
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await repository.AddAsync(supplier, cancellationToken);
        return SupplierDto.FromEntity(supplier);
    }

    public async Task<SupplierDto> UpdateAsync(
        Guid id,
        UpdateSupplierRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = validator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var supplier = await repository.FindByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("供应商不存在或已被删除。");

        var code = SupplierRequestValidator.Normalize(request.Code);
        if (!string.Equals(supplier.Code, code, StringComparison.Ordinal)
            && await repository.CodeExistsAsync(code, supplier.Id, cancellationToken))
        {
            throw new ConflictException($"供应商编码 {code} 已存在。");
        }

        supplier.Code = code;
        supplier.Name = SupplierRequestValidator.Normalize(request.Name);
        supplier.ContactName = Optional(request.ContactName);
        supplier.Phone = Optional(request.Phone);
        supplier.Email = Optional(request.Email);
        supplier.UpdatedAtUtc = DateTime.UtcNow;

        await repository.UpdateAsync(supplier, cancellationToken);
        return SupplierDto.FromEntity(supplier);
    }

    /// <summary>
    /// 停用供应商（软删除），不做物理删除。重复停用保持幂等。
    /// </summary>
    public async Task<SupplierDto> DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var supplier = await repository.FindByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("供应商不存在或已被删除。");

        if (supplier.IsActive)
        {
            supplier.IsActive = false;
            supplier.UpdatedAtUtc = DateTime.UtcNow;
            await repository.UpdateAsync(supplier, cancellationToken);
        }

        return SupplierDto.FromEntity(supplier);
    }

    private static string? Optional(string? value)
    {
        var normalized = SupplierRequestValidator.Normalize(value);
        return normalized.Length == 0 ? null : normalized;
    }
}
