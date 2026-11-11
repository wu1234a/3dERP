using ThreeDErp.Application.Inventory;

namespace ThreeDErp.Api.Endpoints;

/// <summary>
/// /api/v1/inventory/alerts 端点组（实时库存预警）。预警按当前余额与启用规则
/// 实时计算，不落库，也没有确认 / 已读等状态。
/// </summary>
public static class InventoryAlertEndpoints
{
    public static IEndpointRouteBuilder MapInventoryAlertEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/inventory/alerts")
            .WithTags("Inventory");

        group.MapGet(string.Empty, async (
                InventoryAlertService alerts,
                int? page,
                int? pageSize,
                Guid? warehouseId,
                Guid? materialId,
                string? keyword,
                CancellationToken cancellationToken) =>
            {
                var result = await alerts.SearchAlertsAsync(
                    page ?? 1,
                    pageSize ?? 10,
                    warehouseId,
                    materialId,
                    keyword,
                    cancellationToken);

                return Results.Ok(result);
            })
            .WithName("ListInventoryAlerts")
            .WithSummary("实时查询库存预警：当前余额低于启用规则下限的组合");

        return app;
    }
}
