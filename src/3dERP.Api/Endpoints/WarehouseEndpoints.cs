using ThreeDErp.Application.Warehouses;

namespace ThreeDErp.Api.Endpoints;

/// <summary>
/// /api/v1/warehouses 端点组。endpoint 只做参数接收与结果映射，
/// 业务逻辑全部在 WarehouseService 中。
/// </summary>
public static class WarehouseEndpoints
{
    public static IEndpointRouteBuilder MapWarehouseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/warehouses")
            .WithTags("Warehouses");

        group.MapGet(string.Empty, async (
                WarehouseService warehouses,
                int? page,
                int? pageSize,
                string? keyword,
                bool? includeInactive,
                CancellationToken cancellationToken) =>
            {
                var result = await warehouses.SearchAsync(
                    page ?? 1,
                    pageSize ?? 10,
                    keyword,
                    includeInactive ?? false,
                    cancellationToken);

                return Results.Ok(result);
            })
            .WithName("ListWarehouses")
            .WithSummary("分页查询仓库，支持关键字搜索");

        group.MapGet("{id:guid}", async (
                Guid id,
                WarehouseService warehouses,
                CancellationToken cancellationToken) =>
            {
                var warehouse = await warehouses.GetAsync(id, cancellationToken);
                return warehouse is null ? Results.NotFound() : Results.Ok(warehouse);
            })
            .WithName("GetWarehouseById")
            .WithSummary("按 Id 获取仓库");

        group.MapPost(string.Empty, async (
                CreateWarehouseRequest request,
                WarehouseService warehouses,
                CancellationToken cancellationToken) =>
            {
                var warehouse = await warehouses.CreateAsync(request, cancellationToken);
                return Results.CreatedAtRoute("GetWarehouseById", new { id = warehouse.Id }, warehouse);
            })
            .WithName("CreateWarehouse")
            .WithSummary("创建仓库");

        group.MapPut("{id:guid}", async (
                Guid id,
                UpdateWarehouseRequest request,
                WarehouseService warehouses,
                CancellationToken cancellationToken) =>
            {
                var warehouse = await warehouses.UpdateAsync(id, request, cancellationToken);
                return Results.Ok(warehouse);
            })
            .WithName("UpdateWarehouse")
            .WithSummary("更新仓库");

        group.MapDelete("{id:guid}", async (
                Guid id,
                WarehouseService warehouses,
                CancellationToken cancellationToken) =>
            {
                var warehouse = await warehouses.DeactivateAsync(id, cancellationToken);
                return Results.Ok(warehouse);
            })
            .WithName("DeactivateWarehouse")
            .WithSummary("停用仓库（软删除，不做物理删除）");

        return app;
    }
}
