using ThreeDErp.Application.Materials;

namespace ThreeDErp.Api.Endpoints;

/// <summary>
/// /api/v1/materials 端点组。endpoint 只做参数接收与结果映射，
/// 业务逻辑全部在 MaterialService 中。
/// </summary>
public static class MaterialEndpoints
{
    public static IEndpointRouteBuilder MapMaterialEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/materials")
            .WithTags("Materials");

        group.MapGet(string.Empty, async (
                MaterialService materials,
                int? page,
                int? pageSize,
                string? keyword,
                bool? includeInactive,
                CancellationToken cancellationToken) =>
            {
                var result = await materials.SearchAsync(
                    page ?? 1,
                    pageSize ?? 10,
                    keyword,
                    includeInactive ?? false,
                    cancellationToken);

                return Results.Ok(result);
            })
            .WithName("ListMaterials")
            .WithSummary("分页查询物料，支持关键字搜索");

        group.MapGet("{id:guid}", async (
                Guid id,
                MaterialService materials,
                CancellationToken cancellationToken) =>
            {
                var material = await materials.GetAsync(id, cancellationToken);
                return material is null ? Results.NotFound() : Results.Ok(material);
            })
            .WithName("GetMaterialById")
            .WithSummary("按 Id 获取物料");

        group.MapPost(string.Empty, async (
                CreateMaterialRequest request,
                MaterialService materials,
                CancellationToken cancellationToken) =>
            {
                var material = await materials.CreateAsync(request, cancellationToken);
                return Results.CreatedAtRoute("GetMaterialById", new { id = material.Id }, material);
            })
            .WithName("CreateMaterial")
            .WithSummary("创建物料");

        group.MapPut("{id:guid}", async (
                Guid id,
                UpdateMaterialRequest request,
                MaterialService materials,
                CancellationToken cancellationToken) =>
            {
                var material = await materials.UpdateAsync(id, request, cancellationToken);
                return Results.Ok(material);
            })
            .WithName("UpdateMaterial")
            .WithSummary("更新物料");

        group.MapDelete("{id:guid}", async (
                Guid id,
                MaterialService materials,
                CancellationToken cancellationToken) =>
            {
                var material = await materials.DeactivateAsync(id, cancellationToken);
                return Results.Ok(material);
            })
            .WithName("DeactivateMaterial")
            .WithSummary("停用物料（软删除，不做物理删除）");

        return app;
    }
}
