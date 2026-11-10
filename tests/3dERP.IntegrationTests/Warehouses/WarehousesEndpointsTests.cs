using System.Net;
using System.Net.Http.Json;

namespace ThreeDErp.IntegrationTests.Warehouses;

/// <summary>
/// /api/v1/warehouses 集成测试：覆盖 CRUD 快乐路径、重复编码 409、
/// 校验失败 400 与未知 Id 404。每个用例使用独立的内存数据库。
/// </summary>
public sealed class WarehousesEndpointsTests
{
    private static async Task<WarehouseApiModel> CreateWarehouseAsync(
        HttpClient client,
        string code,
        string name = "一号仓库",
        string? address = null,
        string? contactName = null,
        string? phone = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/warehouses", new
        {
            code,
            name,
            address,
            contactName,
            phone
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<WarehouseApiModel>();
        Assert.NotNull(created);
        return created;
    }

    [Fact]
    public async Task CreateWarehouse_ReturnsCreatedAndCanBeFetchedById()
    {
        await using var factory = new WarehousesApiFactory();
        var client = factory.CreateClient();

        var created = await CreateWarehouseAsync(
            client,
            "WH-MAIN-001",
            name: "上海主仓库",
            address: "上海市浦东新区仓库路 1 号",
            contactName: "张三",
            phone: "13800138000");

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("WH-MAIN-001", created.Code);
        Assert.Equal("上海主仓库", created.Name);
        Assert.Equal("上海市浦东新区仓库路 1 号", created.Address);
        Assert.Equal("张三", created.ContactName);
        Assert.Equal("13800138000", created.Phone);
        Assert.True(created.IsActive);

        var fetched = await client.GetFromJsonAsync<WarehouseApiModel>($"/api/v1/warehouses/{created.Id}");

        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal("WH-MAIN-001", fetched.Code);
        Assert.Equal("上海市浦东新区仓库路 1 号", fetched.Address);
    }

    [Fact]
    public async Task ListWarehouses_FiltersByKeywordActiveFlagAndPaging()
    {
        await using var factory = new WarehousesApiFactory();
        var client = factory.CreateClient();

        await CreateWarehouseAsync(client, "WH-MAIN-001", name: "上海主仓库", address: "上海市浦东新区");
        await CreateWarehouseAsync(client, "WH-NORTH-001", name: "北京仓", address: "北京市朝阳区");
        var deactivated = await CreateWarehouseAsync(client, "WH-OLD-001", name: "旧仓", address: "上海市宝山区");
        var deactivateResponse = await client.DeleteAsync($"/api/v1/warehouses/{deactivated.Id}");
        Assert.Equal(HttpStatusCode.OK, deactivateResponse.StatusCode);

        var active = await client.GetFromJsonAsync<PagedApiResult>("/api/v1/warehouses");
        Assert.NotNull(active);
        Assert.Equal(2, active.TotalCount);
        Assert.DoesNotContain(active.Items, item => item.Code == "WH-OLD-001");

        var byCode = await client.GetFromJsonAsync<PagedApiResult>("/api/v1/warehouses?keyword=NORTH");
        Assert.NotNull(byCode);
        var codeItem = Assert.Single(byCode.Items);
        Assert.Equal("WH-NORTH-001", codeItem.Code);

        var byName = await client.GetFromJsonAsync<PagedApiResult>("/api/v1/warehouses?keyword=%E5%8C%97%E4%BA%AC");
        Assert.NotNull(byName);
        var nameItem = Assert.Single(byName.Items);
        Assert.Equal("WH-NORTH-001", nameItem.Code);

        var byAddress = await client.GetFromJsonAsync<PagedApiResult>("/api/v1/warehouses?keyword=%E5%AE%9D%E5%B1%B1&includeInactive=true");
        Assert.NotNull(byAddress);
        var addressItem = Assert.Single(byAddress.Items);
        Assert.Equal("WH-OLD-001", addressItem.Code);

        var includeInactive = await client.GetFromJsonAsync<PagedApiResult>("/api/v1/warehouses?includeInactive=true");
        Assert.NotNull(includeInactive);
        Assert.Equal(3, includeInactive.TotalCount);

        var paged = await client.GetFromJsonAsync<PagedApiResult>("/api/v1/warehouses?page=1&pageSize=1&includeInactive=true");
        Assert.NotNull(paged);
        Assert.Single(paged.Items);
        Assert.Equal(3, paged.TotalCount);
        Assert.Equal(1, paged.PageSize);
    }

    [Fact]
    public async Task UpdateWarehouse_ModifiesFieldsWithoutChangingId()
    {
        await using var factory = new WarehousesApiFactory();
        var client = factory.CreateClient();

        var created = await CreateWarehouseAsync(client, "WH-MAIN-001");

        var response = await client.PutAsJsonAsync($"/api/v1/warehouses/{created.Id}", new
        {
            code = "WH-MAIN-001",
            name = "上海主仓库（扩建）",
            address = "上海市浦东新区仓库路 2 号",
            contactName = "李四",
            phone = "13900139000"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<WarehouseApiModel>();
        Assert.NotNull(updated);
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("上海主仓库（扩建）", updated.Name);
        Assert.Equal("上海市浦东新区仓库路 2 号", updated.Address);
        Assert.Equal("李四", updated.ContactName);
        Assert.Equal("13900139000", updated.Phone);
        Assert.True(updated.IsActive);
    }

    [Fact]
    public async Task UpdateWarehouse_ToExistingCode_ReturnsConflict()
    {
        await using var factory = new WarehousesApiFactory();
        var client = factory.CreateClient();

        var first = await CreateWarehouseAsync(client, "WH-MAIN-001");
        await CreateWarehouseAsync(client, "WH-NORTH-001");

        var response = await client.PutAsJsonAsync($"/api/v1/warehouses/{first.Id}", new
        {
            code = "WH-NORTH-001",
            name = "上海主仓库",
            address = (string?)null,
            contactName = (string?)null,
            phone = (string?)null
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task DeactivateWarehouse_SoftDeletesAndStaysQueryable()
    {
        await using var factory = new WarehousesApiFactory();
        var client = factory.CreateClient();

        var created = await CreateWarehouseAsync(client, "WH-MAIN-001");

        var response = await client.DeleteAsync($"/api/v1/warehouses/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var deactivated = await response.Content.ReadFromJsonAsync<WarehouseApiModel>();
        Assert.NotNull(deactivated);
        Assert.False(deactivated.IsActive);

        // 仍是同一条数据：按 Id 可查询，默认列表不返回，includeInactive 可见。
        var fetched = await client.GetFromJsonAsync<WarehouseApiModel>($"/api/v1/warehouses/{created.Id}");
        Assert.NotNull(fetched);
        Assert.False(fetched.IsActive);

        var active = await client.GetFromJsonAsync<PagedApiResult>("/api/v1/warehouses");
        Assert.NotNull(active);
        Assert.Equal(0, active.TotalCount);

        var includeInactive = await client.GetFromJsonAsync<PagedApiResult>("/api/v1/warehouses?includeInactive=true");
        Assert.NotNull(includeInactive);
        Assert.Equal(1, includeInactive.TotalCount);
    }

    [Fact]
    public async Task CreateWarehouse_DuplicateCode_ReturnsConflict()
    {
        await using var factory = new WarehousesApiFactory();
        var client = factory.CreateClient();

        await CreateWarehouseAsync(client, "WH-MAIN-001");

        var response = await client.PostAsJsonAsync("/api/v1/warehouses", new
        {
            code = "WH-MAIN-001",
            name = "另一个仓库",
            address = (string?)null,
            contactName = (string?)null,
            phone = (string?)null
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CreateWarehouse_InvalidPayload_ReturnsValidationProblem()
    {
        await using var factory = new WarehousesApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/warehouses", new
        {
            code = "wh_01",
            name = "",
            address = new string('a', 257),
            contactName = (string?)null,
            phone = (string?)null
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemApiModel>();
        Assert.NotNull(problem);
        Assert.NotNull(problem.Errors);
        Assert.Contains("code", problem.Errors.Keys);
        Assert.Contains("name", problem.Errors.Keys);
        Assert.Contains("address", problem.Errors.Keys);
    }

    [Fact]
    public async Task UnknownId_ReturnsNotFound()
    {
        await using var factory = new WarehousesApiFactory();
        var client = factory.CreateClient();

        var unknownId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/warehouses/{unknownId}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync($"/api/v1/warehouses/{unknownId}", new
            {
                code = "WH-UNKNOWN-001",
                name = "不存在的仓库",
                address = (string?)null,
                contactName = (string?)null,
                phone = (string?)null
            })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/v1/warehouses/{unknownId}")).StatusCode);
    }

    private sealed record WarehouseApiModel(
        Guid Id,
        string Code,
        string Name,
        string? Address,
        string? ContactName,
        string? Phone,
        bool IsActive,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    private sealed record PagedApiResult(
        List<WarehouseApiModel> Items,
        int Page,
        int PageSize,
        int TotalCount);

    private sealed record ValidationProblemApiModel(Dictionary<string, string[]>? Errors);
}
