using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ThreeDErp.Domain.Inventory;
using ThreeDErp.Domain.Materials;
using ThreeDErp.Domain.Warehouses;
using ThreeDErp.Infrastructure.Persistence;

namespace ThreeDErp.IntegrationTests.Inventory;

/// <summary>
/// /api/v1/inventory/alert-rules 与 /api/v1/inventory/alerts 集成测试：
/// 覆盖规则 CRUD 生命周期、校验（仓库 / 物料存在、下限非负、仓库 × 物料唯一）、
/// 预警计算（低于下限的余额、无余额记录视为 0、停用规则不参与）以及过滤与分页。
/// 数据通过 DbContext 直接播种，宿主使用 EF InMemory。
/// </summary>
public sealed class InventoryAlertEndpointsTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static Warehouse CreateWarehouse(string code, string name) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = name,
        IsActive = true,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static Material CreateMaterial(string code, string name, string unit = "kg") => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = name,
        Unit = unit,
        MaterialType = name.Contains("粉末") ? "金属粉末" : "树脂",
        IsActive = true,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    /// <summary>
    /// 播种 2 仓库 × 3 物料的主数据、余额与预警规则：
    /// WH-A 粉末（余额 8.25 / 下限 10 → 预警）、WH-A 树脂（余额 120.5 / 下限 100 → 不预警）、
    /// WH-A 线材（无余额 / 下限 5 → 预警，现有量视为 0）、WH-B 粉末（停用规则 → 不预警）。
    /// </summary>
    private static async Task<SeedData> SeedAsync(InventoryApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        await context.Database.EnsureCreatedAsync();

        var warehouseA = CreateWarehouse("WH-A", "第一仓库");
        var warehouseB = CreateWarehouse("WH-B", "第二仓库");
        var powder = CreateMaterial("MAT-POWDER-001", "钛合金粉末");
        var resin = CreateMaterial("MAT-RESIN-001", "光敏树脂");
        var filament = CreateMaterial("MAT-FILAMENT-001", "PLA 线材", unit: "m");

        context.Warehouses.AddRange(warehouseA, warehouseB);
        context.Materials.AddRange(powder, resin, filament);

        context.InventoryBalances.AddRange(
            new InventoryBalance
            {
                Id = Guid.NewGuid(),
                WarehouseId = warehouseA.Id,
                MaterialId = powder.Id,
                OnHandQuantity = 8.2500m
            },
            new InventoryBalance
            {
                Id = Guid.NewGuid(),
                WarehouseId = warehouseA.Id,
                MaterialId = resin.Id,
                OnHandQuantity = 120.5000m
            },
            new InventoryBalance
            {
                Id = Guid.NewGuid(),
                WarehouseId = warehouseB.Id,
                MaterialId = resin.Id,
                OnHandQuantity = 45.0000m
            });

        context.InventoryAlertRules.AddRange(
            new InventoryAlertRule
            {
                Id = Guid.NewGuid(),
                WarehouseId = warehouseA.Id,
                MaterialId = powder.Id,
                MinimumQuantity = 10.0000m,
                IsEnabled = true
            },
            new InventoryAlertRule
            {
                Id = Guid.NewGuid(),
                WarehouseId = warehouseA.Id,
                MaterialId = resin.Id,
                MinimumQuantity = 100.0000m,
                IsEnabled = true
            },
            new InventoryAlertRule
            {
                Id = Guid.NewGuid(),
                WarehouseId = warehouseA.Id,
                MaterialId = filament.Id,
                MinimumQuantity = 5.0000m,
                IsEnabled = true
            },
            new InventoryAlertRule
            {
                Id = Guid.NewGuid(),
                WarehouseId = warehouseB.Id,
                MaterialId = powder.Id,
                MinimumQuantity = 3.0000m,
                IsEnabled = false
            });

        await context.SaveChangesAsync();

        return new SeedData
        {
            WarehouseA = warehouseA,
            WarehouseB = warehouseB,
            Powder = powder,
            Resin = resin,
            Filament = filament
        };
    }

    private sealed record SeedData
    {
        public Warehouse WarehouseA { get; init; } = null!;

        public Warehouse WarehouseB { get; init; } = null!;

        public Material Powder { get; init; } = null!;

        public Material Resin { get; init; } = null!;

        public Material Filament { get; init; } = null!;
    }

    [Fact]
    public async Task AlertRuleLifecycle_CreateGetUpdateDelete_WorksOverHttp()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory);

        // 创建（种子中 WH-B × 树脂 没有规则）。
        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/inventory/alert-rules",
            new
            {
                warehouseId = seed.WarehouseB.Id,
                materialId = seed.Resin.Id,
                minimumQuantity = 12.5m,
                isEnabled = true
            });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Equal("application/json", createResponse.Content.Headers.ContentType?.MediaType);

        var created = (await createResponse.Content.ReadFromJsonAsync<AlertRuleApiModel>(WebJson))!;
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal(seed.WarehouseB.Id, created.WarehouseId);
        Assert.Equal("WH-B", created.WarehouseCode);
        Assert.Equal("第二仓库", created.WarehouseName);
        Assert.Equal(seed.Resin.Id, created.MaterialId);
        Assert.Equal("MAT-RESIN-001", created.MaterialCode);
        Assert.Equal("光敏树脂", created.MaterialName);
        Assert.Equal("kg", created.MaterialUnit);
        Assert.Equal(12.5m, created.MinimumQuantity);
        Assert.True(created.IsEnabled);
        Assert.Contains($"/api/v1/inventory/alert-rules/{created.Id}", createResponse.Headers.Location?.ToString());

        // 按 Id 查询
        var fetched = await client.GetFromJsonAsync<AlertRuleApiModel>(
            $"/api/v1/inventory/alert-rules/{created.Id}", WebJson);
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal(12.5m, fetched.MinimumQuantity);

        // 更新
        var updateResponse = await client.PutAsJsonAsync(
            $"/api/v1/inventory/alert-rules/{created.Id}",
            new
            {
                warehouseId = seed.WarehouseB.Id,
                materialId = seed.Resin.Id,
                minimumQuantity = 20m,
                isEnabled = false
            });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updated = (await updateResponse.Content.ReadFromJsonAsync<AlertRuleApiModel>(WebJson))!;
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(20m, updated.MinimumQuantity);
        Assert.False(updated.IsEnabled);

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
            var stored = await context.InventoryAlertRules.SingleAsync(rule => rule.Id == created.Id);
            Assert.Equal(20m, stored.MinimumQuantity);
            Assert.False(stored.IsEnabled);
        }

        // 删除
        var deleteResponse = await client.DeleteAsync($"/api/v1/inventory/alert-rules/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var missingResponse = await client.GetAsync($"/api/v1/inventory/alert-rules/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    [Fact]
    public async Task CreateAlertRule_UnknownWarehouseOrMaterial_ReturnsNotFoundProblem()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory);

        var unknownWarehouse = await client.PostAsJsonAsync(
            "/api/v1/inventory/alert-rules",
            new { warehouseId = Guid.NewGuid(), materialId = seed.Powder.Id, minimumQuantity = 5m });
        Assert.Equal(HttpStatusCode.NotFound, unknownWarehouse.StatusCode);
        Assert.Equal("application/problem+json", unknownWarehouse.Content.Headers.ContentType?.MediaType);

        var unknownMaterial = await client.PostAsJsonAsync(
            "/api/v1/inventory/alert-rules",
            new { warehouseId = seed.WarehouseA.Id, materialId = Guid.NewGuid(), minimumQuantity = 5m });
        Assert.Equal(HttpStatusCode.NotFound, unknownMaterial.StatusCode);

        // 空 Guid 请求体 → 400 校验失败，而非 404。
        var emptyIds = await client.PostAsJsonAsync(
            "/api/v1/inventory/alert-rules",
            new { warehouseId = Guid.Empty, materialId = Guid.Empty, minimumQuantity = 5m });
        Assert.Equal(HttpStatusCode.BadRequest, emptyIds.StatusCode);
        Assert.Equal("application/problem+json", emptyIds.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CreateAlertRule_InvalidMinimumQuantity_ReturnsValidationProblem()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/inventory/alert-rules",
            new { warehouseId = seed.WarehouseA.Id, materialId = seed.Powder.Id, minimumQuantity = -1m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemApiModel>(WebJson))!;
        Assert.NotNull(problem.Errors);
        Assert.True(problem.Errors.ContainsKey("minimumQuantity"));
    }

    [Fact]
    public async Task CreateAlertRule_DuplicateWarehouseMaterialPair_ReturnsConflict()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/inventory/alert-rules",
            new { warehouseId = seed.WarehouseA.Id, materialId = seed.Powder.Id, minimumQuantity = 30m });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UpdateAlertRule_Errors_MapToProblemDetails()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory);

        // 未知规则 → 404。
        var unknownRule = await client.PutAsJsonAsync(
            $"/api/v1/inventory/alert-rules/{Guid.NewGuid()}",
            new { warehouseId = seed.WarehouseA.Id, materialId = seed.Powder.Id, minimumQuantity = 5m });
        Assert.Equal(HttpStatusCode.NotFound, unknownRule.StatusCode);

        // 未知仓库 → 404。
        var existingRules = await client.GetFromJsonAsync<PagedApiModel<AlertRuleApiModel>>(
            "/api/v1/inventory/alert-rules?includeDisabled=true", WebJson);
        var ruleId = existingRules!.Items[0].Id;
        var unknownWarehouse = await client.PutAsJsonAsync(
            $"/api/v1/inventory/alert-rules/{ruleId}",
            new { warehouseId = Guid.NewGuid(), materialId = seed.Powder.Id, minimumQuantity = 5m });
        Assert.Equal(HttpStatusCode.NotFound, unknownWarehouse.StatusCode);

        // 负数下限 → 400。
        var negative = await client.PutAsJsonAsync(
            $"/api/v1/inventory/alert-rules/{ruleId}",
            new { warehouseId = seed.WarehouseA.Id, materialId = seed.Powder.Id, minimumQuantity = -5m });
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
        var problem = (await negative.Content.ReadFromJsonAsync<ValidationProblemApiModel>(WebJson))!;
        Assert.True(problem.Errors.ContainsKey("minimumQuantity"));
    }

    [Fact]
    public async Task UpdateAlertRule_MoveToExistingPair_ReturnsConflict()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory);

        // 新建 WH-B × 树脂 规则，然后把它移动到已有规则的 WH-A × 粉末 组合。
        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/inventory/alert-rules",
            new { warehouseId = seed.WarehouseB.Id, materialId = seed.Resin.Id, minimumQuantity = 5m });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = (await createResponse.Content.ReadFromJsonAsync<AlertRuleApiModel>(WebJson))!;

        var conflict = await client.PutAsJsonAsync(
            $"/api/v1/inventory/alert-rules/{created.Id}",
            new { warehouseId = seed.WarehouseA.Id, materialId = seed.Powder.Id, minimumQuantity = 5m });

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task DeleteAlertRule_UnknownId_ReturnsNotFound()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        await SeedAsync(factory);

        var response = await client.DeleteAsync($"/api/v1/inventory/alert-rules/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListAlertRules_FiltersByWarehouseMaterialAndDisabledFlag()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory);

        var all = await client.GetFromJsonAsync<PagedApiModel<AlertRuleApiModel>>(
            "/api/v1/inventory/alert-rules?includeDisabled=true", WebJson);
        Assert.NotNull(all);
        Assert.Equal(4, all.TotalCount);

        // 稳定排序：仓库编码升序，再按物料编码升序。
        Assert.Collection(all.Items,
            item => Assert.Equal("MAT-FILAMENT-001", item.MaterialCode),
            item => Assert.Equal("MAT-POWDER-001", item.MaterialCode),
            item => Assert.Equal("MAT-RESIN-001", item.MaterialCode),
            item =>
            {
                Assert.Equal("WH-B", item.WarehouseCode);
                Assert.False(item.IsEnabled);
            });

        var enabledOnly = await client.GetFromJsonAsync<PagedApiModel<AlertRuleApiModel>>(
            "/api/v1/inventory/alert-rules", WebJson);
        Assert.NotNull(enabledOnly);
        Assert.Equal(3, enabledOnly.TotalCount);
        Assert.All(enabledOnly.Items, item => Assert.True(item.IsEnabled));

        var byWarehouse = await client.GetFromJsonAsync<PagedApiModel<AlertRuleApiModel>>(
            $"/api/v1/inventory/alert-rules?warehouseId={seed.WarehouseB.Id}&includeDisabled=true", WebJson);
        Assert.NotNull(byWarehouse);
        var warehouseRule = Assert.Single(byWarehouse.Items);
        Assert.Equal(seed.WarehouseB.Id, warehouseRule.WarehouseId);

        var byMaterial = await client.GetFromJsonAsync<PagedApiModel<AlertRuleApiModel>>(
            $"/api/v1/inventory/alert-rules?materialId={seed.Powder.Id}&includeDisabled=true", WebJson);
        Assert.NotNull(byMaterial);
        Assert.Equal(2, byMaterial.TotalCount);
        Assert.All(byMaterial.Items, item => Assert.Equal(seed.Powder.Id, item.MaterialId));
    }

    [Fact]
    public async Task ListAlertRules_InvalidGuidParameter_ReturnsBadRequest()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/inventory/alert-rules?warehouseId=not-a-guid");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ListAlerts_ReturnsBalancesBelowMinimumWithShortage()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory);

        var result = await client.GetFromJsonAsync<PagedApiModel<AlertApiModel>>("/api/v1/inventory/alerts", WebJson);

        Assert.NotNull(result);
        Assert.Equal(2, result.TotalCount);

        // 稳定排序：仓库编码 + 物料编码。线材无余额记录 → 现有量 0。
        Assert.Collection(result.Items,
            item =>
            {
                Assert.Equal("WH-A", item.WarehouseCode);
                Assert.Equal("第一仓库", item.WarehouseName);
                Assert.Equal(seed.Filament.Id, item.MaterialId);
                Assert.Equal("MAT-FILAMENT-001", item.MaterialCode);
                Assert.Equal("PLA 线材", item.MaterialName);
                Assert.Equal("m", item.MaterialUnit);
                Assert.Equal(0m, item.OnHandQuantity);
                Assert.Equal(5m, item.MinimumQuantity);
                Assert.Equal(5m, item.ShortageQuantity);
            },
            item =>
            {
                Assert.Equal("WH-A", item.WarehouseCode);
                Assert.Equal(seed.Powder.Id, item.MaterialId);
                Assert.Equal("MAT-POWDER-001", item.MaterialCode);
                Assert.Equal("钛合金粉末", item.MaterialName);
                Assert.Equal("kg", item.MaterialUnit);
                Assert.Equal(8.25m, item.OnHandQuantity);
                Assert.Equal(10m, item.MinimumQuantity);
                Assert.Equal(1.75m, item.ShortageQuantity);
            });

        // 高于下限（树脂）与停用规则（WH-B 粉末）都不在预警中。
        Assert.DoesNotContain(result.Items, item => item.MaterialId == seed.Resin.Id);
    }

    [Fact]
    public async Task ListAlerts_FiltersByWarehouseAndMaterial()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory);

        var byWarehouse = await client.GetFromJsonAsync<PagedApiModel<AlertApiModel>>(
            $"/api/v1/inventory/alerts?warehouseId={seed.WarehouseA.Id}", WebJson);
        Assert.NotNull(byWarehouse);
        Assert.Equal(2, byWarehouse.TotalCount);
        Assert.All(byWarehouse.Items, item => Assert.Equal(seed.WarehouseA.Id, item.WarehouseId));

        var byMaterial = await client.GetFromJsonAsync<PagedApiModel<AlertApiModel>>(
            $"/api/v1/inventory/alerts?materialId={seed.Powder.Id}", WebJson);
        Assert.NotNull(byMaterial);
        var alert = Assert.Single(byMaterial.Items);
        Assert.Equal(seed.Powder.Id, alert.MaterialId);
        Assert.Equal(1.75m, alert.ShortageQuantity);

        var noMatch = await client.GetFromJsonAsync<PagedApiModel<AlertApiModel>>(
            $"/api/v1/inventory/alerts?warehouseId={seed.WarehouseB.Id}", WebJson);
        Assert.NotNull(noMatch);
        Assert.Empty(noMatch.Items);
        Assert.Equal(0, noMatch.TotalCount);
    }

    [Fact]
    public async Task ListAlerts_AppliesPaging()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        await SeedAsync(factory);

        var firstPage = await client.GetFromJsonAsync<PagedApiModel<AlertApiModel>>(
            "/api/v1/inventory/alerts?page=1&pageSize=1", WebJson);
        Assert.NotNull(firstPage);
        Assert.Single(firstPage.Items);
        Assert.Equal(1, firstPage.Page);
        Assert.Equal(1, firstPage.PageSize);
        Assert.Equal(2, firstPage.TotalCount);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal("MAT-FILAMENT-001", firstPage.Items[0].MaterialCode);

        var secondPage = await client.GetFromJsonAsync<PagedApiModel<AlertApiModel>>(
            "/api/v1/inventory/alerts?page=2&pageSize=1", WebJson);
        Assert.NotNull(secondPage);
        Assert.Equal("MAT-POWDER-001", secondPage.Items[0].MaterialCode);

        // 越界参数收敛到默认值。
        var clamped = await client.GetFromJsonAsync<PagedApiModel<AlertApiModel>>(
            "/api/v1/inventory/alerts?page=0&pageSize=0", WebJson);
        Assert.NotNull(clamped);
        Assert.Equal(1, clamped.Page);
        Assert.Equal(10, clamped.PageSize);
        Assert.Equal(2, clamped.TotalCount);
    }

    [Fact]
    public async Task ListAlerts_EmptyDatabase_ReturnsEmptyPage()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<PagedApiModel<AlertApiModel>>("/api/v1/inventory/alerts", WebJson);

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    private sealed record AlertRuleApiModel(
        Guid Id,
        Guid WarehouseId,
        string WarehouseCode,
        string WarehouseName,
        Guid MaterialId,
        string MaterialCode,
        string MaterialName,
        string MaterialUnit,
        decimal MinimumQuantity,
        bool IsEnabled);

    private sealed record AlertApiModel(
        Guid RuleId,
        Guid WarehouseId,
        string WarehouseCode,
        string WarehouseName,
        Guid MaterialId,
        string MaterialCode,
        string MaterialName,
        string MaterialUnit,
        decimal OnHandQuantity,
        decimal MinimumQuantity,
        decimal ShortageQuantity);

    private sealed record PagedApiModel<T>(
        List<T> Items,
        int Page,
        int PageSize,
        int TotalCount,
        int TotalPages);

    private sealed record ValidationProblemApiModel(Dictionary<string, string[]> Errors);
}
