using ThreeDErp.Application.Common;

namespace ThreeDErp.Application.Inventory;

/// <summary>
/// 库存查询服务。分页 / 关键字规范化在这里完成，不放在 API endpoint 中。
/// </summary>
public sealed class InventoryService(IInventoryRepository repository)
{
    private const int DefaultPageSize = 10;
    private const int MaxPageSize = 100;

    /// <summary>
    /// 分页查询库存余额：warehouseId / materialId 精确过滤，
    /// keyword 匹配物料编码 / 名称（去除首尾空白，空白视为不过滤）。
    /// </summary>
    public async Task<PagedResult<InventoryBalanceDto>> SearchBalancesAsync(
        int page,
        int pageSize,
        Guid? warehouseId,
        Guid? materialId,
        string? keyword,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var normalizedKeyword = Normalize(keyword);
        var result = await repository.FindBalancesAsync(
            page,
            pageSize,
            warehouseId,
            materialId,
            normalizedKeyword.Length == 0 ? null : normalizedKeyword,
            cancellationToken);

        return new PagedResult<InventoryBalanceDto>(
            [.. result.Items.Select(InventoryBalanceDto.FromEntity)],
            result.Page,
            result.PageSize,
            result.TotalCount);
    }

    /// <summary>去除首尾空白；null 视为空字符串。</summary>
    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
}
