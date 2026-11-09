using ThreeDErp.Application.Common;
using ThreeDErp.Domain.Materials;

namespace ThreeDErp.Application.Materials;

/// <summary>
/// 物料仓储端口。Application 层定义契约，Infrastructure 层提供 EF Core 实现。
/// </summary>
public interface IMaterialRepository
{
    Task<PagedResult<Material>> FindAsync(
        int page,
        int pageSize,
        string? keyword,
        bool includeInactive,
        CancellationToken cancellationToken = default);

    Task<Material?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default);

    Task AddAsync(Material material, CancellationToken cancellationToken = default);

    Task UpdateAsync(Material material, CancellationToken cancellationToken = default);
}
