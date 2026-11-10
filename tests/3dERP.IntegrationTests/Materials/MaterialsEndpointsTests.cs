using System.Net;
using System.Net.Http.Json;

namespace ThreeDErp.IntegrationTests.Materials;

/// <summary>
/// /api/v1/materials 集成测试：覆盖 CRUD 快乐路径、重复编码 409、
/// 校验失败 400 与未知 Id 404。每个用例使用独立的内存数据库。
/// </summary>
public sealed class MaterialsEndpointsTests
{
    private static async Task<MaterialApiModel> CreateMaterialAsync(
        HttpClient client,
        string code,
        string name = "光敏树脂",
        string? specification = null,
        string unit = "kg",
        string materialType = "树脂",
        decimal unitPrice = 350.0000m,
        decimal safetyStock = 10.0000m)
    {
        var response = await client.PostAsJsonAsync("/api/v1/materials", new
        {
            code,
            name,
            specification,
            unit,
            materialType,
            unitPrice,
            safetyStock
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<MaterialApiModel>();
        Assert.NotNull(created);
        return created;
    }

    [Fact]
    public async Task CreateMaterial_ReturnsCreatedAndCanBeFetchedById()
    {
        await using var factory = new MaterialsApiFactory();
        var client = factory.CreateClient();

        var created = await CreateMaterialAsync(
            client,
            "MAT-RESIN-001",
            specification: "SLA 通用树脂 1kg 装",
            unitPrice: 350.5m,
            safetyStock: 10.25m);

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("MAT-RESIN-001", created.Code);
        Assert.Equal("光敏树脂", created.Name);
        Assert.Equal("SLA 通用树脂 1kg 装", created.Specification);
        Assert.Equal("kg", created.Unit);
        Assert.Equal("树脂", created.MaterialType);
        Assert.Equal(350.5m, created.UnitPrice);
        Assert.Equal(10.25m, created.SafetyStock);
        Assert.True(created.IsActive);

        var fetched = await client.GetFromJsonAsync<MaterialApiModel>($"/api/v1/materials/{created.Id}");

        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal("MAT-RESIN-001", fetched.Code);
        Assert.Equal(350.5m, fetched.UnitPrice);
    }

    [Fact]
    public async Task ListMaterials_FiltersByKeywordActiveFlagAndPaging()
    {
        await using var factory = new MaterialsApiFactory();
        var client = factory.CreateClient();

        await CreateMaterialAsync(client, "MAT-RESIN-001", name: "光敏树脂");
        await CreateMaterialAsync(client, "MAT-POWDER-001", name: "钛合金粉末", materialType: "金属粉末");
        var deactivated = await CreateMaterialAsync(client, "MAT-RESIN-002", name: "旧款树脂");
        var deactivateResponse = await client.DeleteAsync($"/api/v1/materials/{deactivated.Id}");
        Assert.Equal(HttpStatusCode.OK, deactivateResponse.StatusCode);

        var active = await client.GetFromJsonAsync<PagedApiResult>("/api/v1/materials");
        Assert.NotNull(active);
        Assert.Equal(2, active.TotalCount);
        Assert.DoesNotContain(active.Items, item => item.Code == "MAT-RESIN-002");

        var byCode = await client.GetFromJsonAsync<PagedApiResult>("/api/v1/materials?keyword=POWDER");
        Assert.NotNull(byCode);
        var codeItem = Assert.Single(byCode.Items);
        Assert.Equal("MAT-POWDER-001", codeItem.Code);

        var byName = await client.GetFromJsonAsync<PagedApiResult>("/api/v1/materials?keyword=%E6%A0%91%E8%84%82");
        Assert.NotNull(byName);
        var nameItem = Assert.Single(byName.Items);
        Assert.Equal("MAT-RESIN-001", nameItem.Code);

        var byType = await client.GetFromJsonAsync<PagedApiResult>("/api/v1/materials?keyword=%E9%87%91%E5%B1%9E");
        Assert.NotNull(byType);
        var typeItem = Assert.Single(byType.Items);
        Assert.Equal("MAT-POWDER-001", typeItem.Code);

        var includeInactive = await client.GetFromJsonAsync<PagedApiResult>("/api/v1/materials?includeInactive=true");
        Assert.NotNull(includeInactive);
        Assert.Equal(3, includeInactive.TotalCount);

        var paged = await client.GetFromJsonAsync<PagedApiResult>("/api/v1/materials?page=1&pageSize=1&includeInactive=true");
        Assert.NotNull(paged);
        Assert.Single(paged.Items);
        Assert.Equal(3, paged.TotalCount);
        Assert.Equal(1, paged.PageSize);
    }

    [Fact]
    public async Task UpdateMaterial_ModifiesFieldsWithoutChangingId()
    {
        await using var factory = new MaterialsApiFactory();
        var client = factory.CreateClient();

        var created = await CreateMaterialAsync(client, "MAT-RESIN-001");

        var response = await client.PutAsJsonAsync($"/api/v1/materials/{created.Id}", new
        {
            code = "MAT-RESIN-001",
            name = "高强度树脂",
            specification = "工业级",
            unit = "g",
            materialType = "树脂",
            unitPrice = 420.0000m,
            safetyStock = 25.5000m
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<MaterialApiModel>();
        Assert.NotNull(updated);
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("高强度树脂", updated.Name);
        Assert.Equal("工业级", updated.Specification);
        Assert.Equal("g", updated.Unit);
        Assert.Equal(420.0000m, updated.UnitPrice);
        Assert.Equal(25.5000m, updated.SafetyStock);
        Assert.True(updated.IsActive);
    }

    [Fact]
    public async Task UpdateMaterial_ToExistingCode_ReturnsConflict()
    {
        await using var factory = new MaterialsApiFactory();
        var client = factory.CreateClient();

        var first = await CreateMaterialAsync(client, "MAT-RESIN-001");
        await CreateMaterialAsync(client, "MAT-POWDER-001");

        var response = await client.PutAsJsonAsync($"/api/v1/materials/{first.Id}", new
        {
            code = "MAT-POWDER-001",
            name = "高强度树脂",
            specification = "工业级",
            unit = "kg",
            materialType = "树脂",
            unitPrice = 420.0000m,
            safetyStock = 25.5000m
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task DeactivateMaterial_SoftDeletesAndStaysQueryable()
    {
        await using var factory = new MaterialsApiFactory();
        var client = factory.CreateClient();

        var created = await CreateMaterialAsync(client, "MAT-RESIN-001");

        var response = await client.DeleteAsync($"/api/v1/materials/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var deactivated = await response.Content.ReadFromJsonAsync<MaterialApiModel>();
        Assert.NotNull(deactivated);
        Assert.False(deactivated.IsActive);

        // 仍是同一条数据：按 Id 可查询，默认列表不返回，includeInactive 可见。
        var fetched = await client.GetFromJsonAsync<MaterialApiModel>($"/api/v1/materials/{created.Id}");
        Assert.NotNull(fetched);
        Assert.False(fetched.IsActive);

        var active = await client.GetFromJsonAsync<PagedApiResult>("/api/v1/materials");
        Assert.NotNull(active);
        Assert.Equal(0, active.TotalCount);

        var includeInactive = await client.GetFromJsonAsync<PagedApiResult>("/api/v1/materials?includeInactive=true");
        Assert.NotNull(includeInactive);
        Assert.Equal(1, includeInactive.TotalCount);
    }

    [Fact]
    public async Task CreateMaterial_DuplicateCode_ReturnsConflict()
    {
        await using var factory = new MaterialsApiFactory();
        var client = factory.CreateClient();

        await CreateMaterialAsync(client, "MAT-RESIN-001");

        var response = await client.PostAsJsonAsync("/api/v1/materials", new
        {
            code = "MAT-RESIN-001",
            name = "另一种树脂",
            specification = (string?)null,
            unit = "kg",
            materialType = "树脂",
            unitPrice = 100.0000m,
            safetyStock = 5.0000m
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CreateMaterial_InvalidPayload_ReturnsValidationProblem()
    {
        await using var factory = new MaterialsApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/materials", new
        {
            code = "mat_01",
            name = "",
            specification = (string?)null,
            unit = "",
            materialType = "树脂",
            unitPrice = -1m,
            safetyStock = 10.0000m
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemApiModel>();
        Assert.NotNull(problem);
        Assert.NotNull(problem.Errors);
        Assert.Contains("code", problem.Errors.Keys);
        Assert.Contains("name", problem.Errors.Keys);
        Assert.Contains("unit", problem.Errors.Keys);
        Assert.Contains("unitPrice", problem.Errors.Keys);
    }

    [Fact]
    public async Task UnknownId_ReturnsNotFound()
    {
        await using var factory = new MaterialsApiFactory();
        var client = factory.CreateClient();

        var unknownId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/materials/{unknownId}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync($"/api/v1/materials/{unknownId}", new
            {
                code = "MAT-RESIN-009",
                name = "不存在的树脂",
                specification = (string?)null,
                unit = "kg",
                materialType = "树脂",
                unitPrice = 1m,
                safetyStock = 1m
            })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/v1/materials/{unknownId}")).StatusCode);
    }

    private sealed record MaterialApiModel(
        Guid Id,
        string Code,
        string Name,
        string? Specification,
        string Unit,
        string MaterialType,
        decimal UnitPrice,
        decimal SafetyStock,
        bool IsActive,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    private sealed record PagedApiResult(
        List<MaterialApiModel> Items,
        int Page,
        int PageSize,
        int TotalCount);

    private sealed record ValidationProblemApiModel(Dictionary<string, string[]>? Errors);
}
