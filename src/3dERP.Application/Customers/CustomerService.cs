using ThreeDErp.Application.Common;
using ThreeDErp.Domain.Customers;

namespace ThreeDErp.Application.Customers;

/// <summary>
/// 客户主数据查询 / 写入服务。校验、去重、时间戳等业务逻辑集中在这里，
/// 不放在 API endpoint 中。
/// </summary>
public sealed class CustomerService(
    ICustomerRepository repository,
    CustomerRequestValidator validator)
{
    private const int DefaultPageSize = 10;
    private const int MaxPageSize = 100;

    public async Task<PagedResult<CustomerDto>> SearchAsync(
        int page,
        int pageSize,
        string? keyword,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var normalizedKeyword = CustomerRequestValidator.Normalize(keyword);
        var result = await repository.FindAsync(
            page,
            pageSize,
            normalizedKeyword.Length == 0 ? null : normalizedKeyword,
            includeInactive,
            cancellationToken);

        return new PagedResult<CustomerDto>(
            [.. result.Items.Select(CustomerDto.FromEntity)],
            result.Page,
            result.PageSize,
            result.TotalCount);
    }

    public async Task<CustomerDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var customer = await repository.FindByIdAsync(id, cancellationToken);
        return customer is null ? null : CustomerDto.FromEntity(customer);
    }

    public async Task<CustomerDto> CreateAsync(
        CreateCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = validator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var code = CustomerRequestValidator.Normalize(request.Code);
        if (await repository.CodeExistsAsync(code, null, cancellationToken))
        {
            throw new ConflictException($"客户编码 {code} 已存在。");
        }

        var now = DateTime.UtcNow;
        var customer = new Customer
        {
            Id = Guid.CreateVersion7(),
            Code = code,
            Name = CustomerRequestValidator.Normalize(request.Name),
            ContactName = Optional(request.ContactName),
            Phone = Optional(request.Phone),
            Email = Optional(request.Email),
            Address = Optional(request.Address),
            TaxNo = Optional(request.TaxNo),
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await repository.AddAsync(customer, cancellationToken);
        return CustomerDto.FromEntity(customer);
    }

    public async Task<CustomerDto> UpdateAsync(
        Guid id,
        UpdateCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = validator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var customer = await repository.FindByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("客户不存在或已被删除。");

        var code = CustomerRequestValidator.Normalize(request.Code);
        if (!string.Equals(customer.Code, code, StringComparison.Ordinal)
            && await repository.CodeExistsAsync(code, customer.Id, cancellationToken))
        {
            throw new ConflictException($"客户编码 {code} 已存在。");
        }

        customer.Code = code;
        customer.Name = CustomerRequestValidator.Normalize(request.Name);
        customer.ContactName = Optional(request.ContactName);
        customer.Phone = Optional(request.Phone);
        customer.Email = Optional(request.Email);
        customer.Address = Optional(request.Address);
        customer.TaxNo = Optional(request.TaxNo);
        customer.UpdatedAtUtc = DateTime.UtcNow;

        await repository.UpdateAsync(customer, cancellationToken);
        return CustomerDto.FromEntity(customer);
    }

    /// <summary>
    /// 停用客户（软删除），不做物理删除。重复停用保持幂等。
    /// </summary>
    public async Task<CustomerDto> DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var customer = await repository.FindByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("客户不存在或已被删除。");

        if (customer.IsActive)
        {
            customer.IsActive = false;
            customer.UpdatedAtUtc = DateTime.UtcNow;
            await repository.UpdateAsync(customer, cancellationToken);
        }

        return CustomerDto.FromEntity(customer);
    }

    private static string? Optional(string? value)
    {
        var normalized = CustomerRequestValidator.Normalize(value);
        return normalized.Length == 0 ? null : normalized;
    }
}
