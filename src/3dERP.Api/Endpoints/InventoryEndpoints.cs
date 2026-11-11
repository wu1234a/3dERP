using ThreeDErp.Application.Inventory;

namespace ThreeDErp.Api.Endpoints;

/// <summary>
/// /api/v1/inventory 端点组。endpoint 只做参数接收与结果映射，
/// 业务逻辑全部在 InventoryService 中。
/// </summary>
public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/inventory")
            .WithTags("Inventory");

        group.MapGet("/balances", async (
                InventoryService inventory,
                int? page,
                int? pageSize,
                Guid? warehouseId,
                Guid? materialId,
                string? keyword,
                CancellationToken cancellationToken) =>
            {
                var result = await inventory.SearchBalancesAsync(
                    page ?? 1,
                    pageSize ?? 10,
                    warehouseId,
                    materialId,
                    keyword,
                    cancellationToken);

                return Results.Ok(result);
            })
            .WithName("ListInventoryBalances")
            .WithSummary("分页查询库存余额，支持仓库 / 物料过滤和物料关键字");

        return app;
    }
}
