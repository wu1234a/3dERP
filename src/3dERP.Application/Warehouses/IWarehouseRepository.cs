using ThreeDErp.Application.Common;
using ThreeDErp.Domain.Warehouses;

namespace ThreeDErp.Application.Warehouses;

/// <summary>
/// 仓库仓储端口。Application 层定义契约，Infrastructure 层提供 EF Core 实现。
/// </summary>
public interface IWarehouseRepository
{
    Task<PagedResult<Warehouse>> FindAsync(
        int page,
        int pageSize,
        string? keyword,
        bool includeInactive,
        CancellationToken cancellationToken = default);

    Task<Warehouse?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default);

    Task AddAsync(Warehouse warehouse, CancellationToken cancellationToken = default);

    Task UpdateAsync(Warehouse warehouse, CancellationToken cancellationToken = default);
}
