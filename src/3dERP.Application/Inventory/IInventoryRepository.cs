using ThreeDErp.Application.Common;
using ThreeDErp.Domain.Inventory;

namespace ThreeDErp.Application.Inventory;

/// <summary>
/// 库存仓储端口。Application 层定义契约，Infrastructure 层提供 EF Core 实现。
/// 查询返回的余额已加载 Warehouse / Material 导航属性，供服务层映射主数据摘要。
/// </summary>
public interface IInventoryRepository
{
    /// <summary>
    /// 分页查询库存余额，可按仓库、物料过滤，keyword 匹配物料编码 / 名称。
    /// </summary>
    Task<PagedResult<InventoryBalance>> FindBalancesAsync(
        int page,
        int pageSize,
        Guid? warehouseId,
        Guid? materialId,
        string? keyword,
        CancellationToken cancellationToken = default);
}
