using ThreeDErp.Application.Inventory;

namespace ThreeDErp.Api.Endpoints;

/// <summary>
/// /api/v1/inventory 端点组。endpoint 只做参数接收与结果映射，
/// 业务逻辑全部在 InventoryService、InventoryAlertService 和 InventoryTransactionService 中。
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

        group.MapGet("/alerts", async (
                InventoryAlertService alerts,
                int? page,
                int? pageSize,
                Guid? warehouseId,
                Guid? materialId,
                CancellationToken cancellationToken) =>
            {
                var result = await alerts.SearchAlertsAsync(
                    page ?? 1,
                    pageSize ?? 10,
                    warehouseId,
                    materialId,
                    cancellationToken);

                return Results.Ok(result);
            })
            .WithName("ListInventoryAlerts")
            .WithSummary("查询库存预警：启用规则中余额低于预警下限的组合，含缺口数量");

        var alertRules = group.MapGroup("/alert-rules");

        alertRules.MapGet(string.Empty, async (
                InventoryAlertService alerts,
                int? page,
                int? pageSize,
                Guid? warehouseId,
                Guid? materialId,
                bool? includeDisabled,
                CancellationToken cancellationToken) =>
            {
                var result = await alerts.SearchRulesAsync(
                    page ?? 1,
                    pageSize ?? 10,
                    warehouseId,
                    materialId,
                    includeDisabled ?? false,
                    cancellationToken);

                return Results.Ok(result);
            })
            .WithName("ListInventoryAlertRules")
            .WithSummary("分页查询库存预警规则，支持仓库 / 物料过滤");

        alertRules.MapGet("{id:guid}", async (
                Guid id,
                InventoryAlertService alerts,
                CancellationToken cancellationToken) =>
            {
                var rule = await alerts.GetRuleAsync(id, cancellationToken);
                return rule is null ? Results.NotFound() : Results.Ok(rule);
            })
            .WithName("GetInventoryAlertRuleById")
            .WithSummary("按 Id 获取库存预警规则");

        alertRules.MapPost(string.Empty, async (
                CreateInventoryAlertRuleRequest request,
                InventoryAlertService alerts,
                CancellationToken cancellationToken) =>
            {
                var rule = await alerts.CreateRuleAsync(request, cancellationToken);
                return Results.CreatedAtRoute(
                    "GetInventoryAlertRuleById",
                    new { id = rule.Id },
                    rule);
            })
            .WithName("CreateInventoryAlertRule")
            .WithSummary("创建库存预警规则（校验仓库 / 物料存在与组合唯一）");

        alertRules.MapPut("{id:guid}", async (
                Guid id,
                UpdateInventoryAlertRuleRequest request,
                InventoryAlertService alerts,
                CancellationToken cancellationToken) =>
            {
                var rule = await alerts.UpdateRuleAsync(id, request, cancellationToken);
                return Results.Ok(rule);
            })
            .WithName("UpdateInventoryAlertRule")
            .WithSummary("更新库存预警规则");

        alertRules.MapDelete("{id:guid}", async (
                Guid id,
                InventoryAlertService alerts,
                CancellationToken cancellationToken) =>
            {
                await alerts.DeleteRuleAsync(id, cancellationToken);
                return Results.NoContent();
            })
            .WithName("DeleteInventoryAlertRule")
            .WithSummary("删除库存预警规则（配置数据，物理删除）");
        group.MapPost("/inbound", async (
                InventoryTransactionRequest request,
                InventoryTransactionService inventory,
                CancellationToken cancellationToken) =>
            {
                var movement = await inventory.RecordInboundAsync(request, cancellationToken);
                return Results.Created($"/api/v1/inventory/movements/{movement.Id}", movement);
            })
            .WithName("RecordInventoryInbound")
            .WithSummary("库存入库：同一事务内累加余额并写入流水，禁止负库存，重复单据号返回 409");

        group.MapPost("/outbound", async (
                InventoryTransactionRequest request,
                InventoryTransactionService inventory,
                CancellationToken cancellationToken) =>
            {
                var movement = await inventory.RecordOutboundAsync(request, cancellationToken);
                return Results.Created($"/api/v1/inventory/movements/{movement.Id}", movement);
            })
            .WithName("RecordInventoryOutbound")
            .WithSummary("库存出库：同一事务内扣减余额并写入流水，库存不足 / 重复单据号返回 409");

        return app;
    }
}
