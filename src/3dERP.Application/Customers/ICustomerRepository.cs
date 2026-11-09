using ThreeDErp.Application.Common;
using ThreeDErp.Domain.Customers;

namespace ThreeDErp.Application.Customers;

/// <summary>
/// 客户仓储端口。Application 层定义契约，Infrastructure 层提供 EF Core 实现。
/// </summary>
public interface ICustomerRepository
{
    Task<PagedResult<Customer>> FindAsync(
        int page,
        int pageSize,
        string? keyword,
        bool includeInactive,
        CancellationToken cancellationToken = default);

    Task<Customer?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default);

    Task AddAsync(Customer customer, CancellationToken cancellationToken = default);

    Task UpdateAsync(Customer customer, CancellationToken cancellationToken = default);
}
