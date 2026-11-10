using ThreeDErp.Application.Common;
using ThreeDErp.Domain.Suppliers;

namespace ThreeDErp.Application.Suppliers;

/// <summary>
/// 供应商仓储端口。Application 层定义契约，Infrastructure 层提供 EF Core 实现。
/// </summary>
public interface ISupplierRepository
{
    Task<PagedResult<Supplier>> FindAsync(
        int page,
        int pageSize,
        string? keyword,
        bool includeInactive,
        CancellationToken cancellationToken = default);

    Task<Supplier?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default);

    Task AddAsync(Supplier supplier, CancellationToken cancellationToken = default);

    Task UpdateAsync(Supplier supplier, CancellationToken cancellationToken = default);
}
