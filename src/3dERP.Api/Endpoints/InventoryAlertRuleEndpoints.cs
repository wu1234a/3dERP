using ThreeDErp.Application.Inventory;

namespace ThreeDErp.Api.Endpoints;

/// <summary>
/// /api/v1/inventory/alert-rules 端点组（预警规则管理）。endpoint 只做参数接收与
/// 结果映射，业务逻辑全部在 InventoryAlertService 中。
/// </summary>
public static class InventoryAlertRuleEndpoints
{
    public static IEndpointRouteBuilder MapInventoryAlertRuleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/inventory/alert-rules")
            .WithTags("Inventory");

        group.MapGet(string.Empty, async (
                InventoryAlertService alerts,
                int? page,
                int? pageSize,
                Guid? warehouseId,
                Guid? materialId,
                bool? isEnabled,
                string? keyword,
                CancellationToken cancellationToken) =>
            {
                var result = await alerts.SearchRulesAsync(
                    page ?? 1,
                    pageSize ?? 10,
                    warehouseId,
                    materialId,
                    isEnabled,
                    keyword,
                    cancellationToken);

                return Results.Ok(result);
            })
            .WithName("ListInventoryAlertRules")
            .WithSummary("分页查询库存预警规则，支持仓库 / 物料 / 启停过滤和物料关键字");

        group.MapGet("{id:guid}", async (
                Guid id,
                InventoryAlertService alerts,
                CancellationToken cancellationToken) =>
            {
                var rule = await alerts.GetRuleAsync(id, cancellationToken);
                return rule is null ? Results.NotFound() : Results.Ok(rule);
            })
            .WithName("GetInventoryAlertRuleById")
            .WithSummary("按 Id 获取库存预警规则");

        group.MapPost(string.Empty, async (
                CreateInventoryAlertRuleRequest request,
                InventoryAlertService alerts,
                CancellationToken cancellationToken) =>
            {
                var rule = await alerts.CreateRuleAsync(request, cancellationToken);
                return Results.CreatedAtRoute("GetInventoryAlertRuleById", new { id = rule.Id }, rule);
            })
            .WithName("CreateInventoryAlertRule")
            .WithSummary("创建库存预警规则（每个仓库 × 物料组合仅一条）");

        group.MapPut("{id:guid}", async (
                Guid id,
                UpdateInventoryAlertRuleRequest request,
                InventoryAlertService alerts,
                CancellationToken cancellationToken) =>
            {
                var rule = await alerts.UpdateRuleAsync(id, request, cancellationToken);
                return Results.Ok(rule);
            })
            .WithName("UpdateInventoryAlertRule")
            .WithSummary("更新库存预警规则（编辑阈值 / 适用范围，同时支持启停）");

        return app;
    }
}
