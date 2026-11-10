using ThreeDErp.Application.Suppliers;

namespace ThreeDErp.Api.Endpoints;

/// <summary>
/// /api/v1/suppliers 端点组。endpoint 只做参数接收与结果映射，
/// 业务逻辑全部在 SupplierService 中。
/// </summary>
public static class SupplierEndpoints
{
    public static IEndpointRouteBuilder MapSupplierEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/suppliers")
            .WithTags("Suppliers");

        group.MapGet(string.Empty, async (
                SupplierService suppliers,
                int? page,
                int? pageSize,
                string? keyword,
                bool? includeInactive,
                CancellationToken cancellationToken) =>
            {
                var result = await suppliers.SearchAsync(
                    page ?? 1,
                    pageSize ?? 10,
                    keyword,
                    includeInactive ?? false,
                    cancellationToken);

                return Results.Ok(result);
            })
            .WithName("ListSuppliers")
            .WithSummary("分页查询供应商，支持关键字搜索");

        group.MapGet("{id:guid}", async (
                Guid id,
                SupplierService suppliers,
                CancellationToken cancellationToken) =>
            {
                var supplier = await suppliers.GetAsync(id, cancellationToken);
                return supplier is null ? Results.NotFound() : Results.Ok(supplier);
            })
            .WithName("GetSupplierById")
            .WithSummary("按 Id 获取供应商");

        group.MapPost(string.Empty, async (
                CreateSupplierRequest request,
                SupplierService suppliers,
                CancellationToken cancellationToken) =>
            {
                var supplier = await suppliers.CreateAsync(request, cancellationToken);
                return Results.CreatedAtRoute("GetSupplierById", new { id = supplier.Id }, supplier);
            })
            .WithName("CreateSupplier")
            .WithSummary("创建供应商");

        group.MapPut("{id:guid}", async (
                Guid id,
                UpdateSupplierRequest request,
                SupplierService suppliers,
                CancellationToken cancellationToken) =>
            {
                var supplier = await suppliers.UpdateAsync(id, request, cancellationToken);
                return Results.Ok(supplier);
            })
            .WithName("UpdateSupplier")
            .WithSummary("更新供应商");

        group.MapDelete("{id:guid}", async (
                Guid id,
                SupplierService suppliers,
                CancellationToken cancellationToken) =>
            {
                var supplier = await suppliers.DeactivateAsync(id, cancellationToken);
                return Results.Ok(supplier);
            })
            .WithName("DeactivateSupplier")
            .WithSummary("停用供应商（软删除，不做物理删除）");

        return app;
    }
}
