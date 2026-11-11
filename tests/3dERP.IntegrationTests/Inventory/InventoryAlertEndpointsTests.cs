using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ThreeDErp.Domain.Inventory;
using ThreeDErp.Domain.Materials;
using ThreeDErp.Domain.Warehouses;
using ThreeDErp.Infrastructure.Persistence;

namespace ThreeDErp.IntegrationTests.Inventory;

/// <summary>
/// /api/v1/inventory/alert-rules 与 /api/v1/inventory/alerts 集成测试：
/// 规则新增 / 查询 / 修改（含启停）、重复规则与无效标识处理、校验失败、
/// 阈值边界（余额等于下限不预警）、停用规则排除、空结果，以及
/// "规则变更后再次查询即反映"的实时性（不依赖出入库流水）。
/// 数据通过 DbContext 直接播种，走真实路由 / 模型绑定 / ProblemDetails 管道。
/// </summary>
public sealed class InventoryAlertEndpointsTests
{
    private sealed class SeedData
    {
        public Warehouse MainWarehouse { get; init; } = null!;

        public Warehouse SecondWarehouse { get; init; } = null!;

        public Material Powder { get; init; } = null!;

        public Material Resin { get; init; } = null!;

        /// <summary>WH-A × 粉末，启用，下限 10，余额 8.25 → 预警。</summary>
        public InventoryAlertRule PowderRule { get; init; } = null!;

        /// <summary>WH-A × 树脂，启用，下限 150，余额 120.5 → 预警。</summary>
        public InventoryAlertRule ResinRule { get; init; } = null!;

        /// <summary>WH-B × 树脂，启用，下限 45 与余额相等（边界）→ 不预警。</summary>
        public InventoryAlertRule BoundaryRule { get; init; } = null!;

        /// <summary>WH-B × 粉末，停用（余额 1 低于下限 5）→ 不预警。</summary>
        public InventoryAlertRule DisabledRule { get; init; } = null!;
    }

    private static Warehouse CreateWarehouse(string code, string name) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = name,
        IsActive = true,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static Material CreateMaterial(string code, string name) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = name,
        Unit = "kg",
        MaterialType = name.Contains("粉末") ? "金属粉末" : "树脂",
        IsActive = true,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    /// <summary>
    /// 播种 2 仓库 × 2 物料的余额与 4 条规则（含边界与停用规则）。
    /// 初始预警预期：WH-A 粉末（8.25 &lt; 10）与 WH-A 树脂（120.5 &lt; 150）共 2 条。
    /// </summary>
    private static async Task<SeedData> SeedAsync(InventoryApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        await context.Database.EnsureCreatedAsync();

        var mainWarehouse = CreateWarehouse("WH-A", "第一仓库");
        var secondWarehouse = CreateWarehouse("WH-B", "第二仓库");
        var powder = CreateMaterial("MAT-POWDER-001", "钛合金粉末");
        var resin = CreateMaterial("MAT-RESIN-001", "光敏树脂");

        context.Warehouses.AddRange(mainWarehouse, secondWarehouse);
        context.Materials.AddRange(powder, resin);

        context.InventoryBalances.AddRange(
            new InventoryBalance
            {
                Id = Guid.NewGuid(),
                WarehouseId = mainWarehouse.Id,
                MaterialId = powder.Id,
                OnHandQuantity = 8.2500m
            },
            new InventoryBalance
            {
                Id = Guid.NewGuid(),
                WarehouseId = mainWarehouse.Id,
                MaterialId = resin.Id,
                OnHandQuantity = 120.5000m
            },
            new InventoryBalance
            {
                Id = Guid.NewGuid(),
                WarehouseId = secondWarehouse.Id,
                MaterialId = resin.Id,
                OnHandQuantity = 45.0000m
            },
            new InventoryBalance
            {
                Id = Guid.NewGuid(),
                WarehouseId = secondWarehouse.Id,
                MaterialId = powder.Id,
                OnHandQuantity = 1.0000m
            });

        var powderRule = new InventoryAlertRule
        {
            Id = Guid.NewGuid(),
            WarehouseId = mainWarehouse.Id,
            MaterialId = powder.Id,
            MinimumQuantity = 10.0000m,
            IsEnabled = true
        };
        var resinRule = new InventoryAlertRule
        {
            Id = Guid.NewGuid(),
            WarehouseId = mainWarehouse.Id,
            MaterialId = resin.Id,
            MinimumQuantity = 150.0000m,
            IsEnabled = true
        };
        var boundaryRule = new InventoryAlertRule
        {
            Id = Guid.NewGuid(),
            WarehouseId = secondWarehouse.Id,
            MaterialId = resin.Id,
            MinimumQuantity = 45.0000m,
            IsEnabled = true
        };
        var disabledRule = new InventoryAlertRule
        {
            Id = Guid.NewGuid(),
            WarehouseId = secondWarehouse.Id,
            MaterialId = powder.Id,
            MinimumQuantity = 5.0000m,
            IsEnabled = false
        };

        context.InventoryAlertRules.AddRange(powderRule, resinRule, boundaryRule, disabledRule);
        await context.SaveChangesAsync();

        return new SeedData
        {
            MainWarehouse = mainWarehouse,
            SecondWarehouse = secondWarehouse,
            Powder = powder,
            Resin = resin,
            PowderRule = powderRule,
            ResinRule = resinRule,
            BoundaryRule = boundaryRule,
            DisabledRule = disabledRule
        };
    }

    [Fact]
    public async Task ListAlertRules_EmptyDatabase_ReturnsEmptyPage()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<RulePagedApiResult>("/api/v1/inventory/alert-rules");

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public async Task ListAlerts_EmptyDatabase_ReturnsEmptyCollection()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<AlertPagedApiResult>("/api/v1/inventory/alerts");

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task ListAlerts_NoMatchingAlerts_ReturnsEmptyCollection()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        await SeedAsync(factory);

        var result = await client.GetFromJsonAsync<AlertPagedApiResult>(
            "/api/v1/inventory/alerts?keyword=%E4%B8%8D%E5%AD%98%E5%9C%A8");

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task ListAlerts_ReturnsRealtimeAlertsWithDimensionRuleThresholdAndType()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory);

        var result = await client.GetFromJsonAsync<AlertPagedApiResult>("/api/v1/inventory/alerts");

        Assert.NotNull(result);
        Assert.Equal(2, result.TotalCount);

        // 稳定排序：仓库编码升序，再按物料编码升序。余额等于下限（WH-B 树脂）与
        // 停用规则（WH-B 粉末）都不在结果中。
        Assert.Collection(result.Items,
            alert =>
            {
                Assert.Equal(seed.MainWarehouse.Id, alert.WarehouseId);
                Assert.Equal("WH-A", alert.WarehouseCode);
                Assert.Equal("第一仓库", alert.WarehouseName);
                Assert.Equal(seed.Powder.Id, alert.MaterialId);
                Assert.Equal("MAT-POWDER-001", alert.MaterialCode);
                Assert.Equal("钛合金粉末", alert.MaterialName);
                Assert.Equal("kg", alert.MaterialUnit);
                Assert.Equal(8.2500m, alert.OnHandQuantity);
                Assert.Equal(seed.PowderRule.Id, alert.RuleId);
                Assert.Equal(10.0000m, alert.MinimumQuantity);
                Assert.Equal("lowStock", alert.AlertType);
            },
            alert =>
            {
                Assert.Equal("WH-A", alert.WarehouseCode);
                Assert.Equal(seed.Resin.Id, alert.MaterialId);
                Assert.Equal("MAT-RESIN-001", alert.MaterialCode);
                Assert.Equal(120.5000m, alert.OnHandQuantity);
                Assert.Equal(seed.ResinRule.Id, alert.RuleId);
                Assert.Equal(150.0000m, alert.MinimumQuantity);
                Assert.Equal("lowStock", alert.AlertType);
            });

        // 边界：余额等于下限不预警；停用规则不参与预警。
        Assert.DoesNotContain(result.Items, alert => alert.RuleId == seed.BoundaryRule.Id);
        Assert.DoesNotContain(result.Items, alert => alert.RuleId == seed.DisabledRule.Id);
    }

    [Fact]
    public async Task ListAlerts_BalanceAtThreshold_IsExcludedUntilThresholdRaised()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory);

        var before = await client.GetFromJsonAsync<AlertPagedApiResult>(
            $"/api/v1/inventory/alerts?warehouseId={seed.SecondWarehouse.Id}");
        Assert.NotNull(before);
        Assert.Empty(before.Items);

        // 阈值提高到余额之上后，同一余额即触发预警。
        var update = await client.PutAsJsonAsync(
            $"/api/v1/inventory/alert-rules/{seed.BoundaryRule.Id}",
            new
            {
                warehouseId = seed.SecondWarehouse.Id,
                materialId = seed.Resin.Id,
                minimumQuantity = 45.0001m,
                isEnabled = true
            });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var after = await client.GetFromJsonAsync<AlertPagedApiResult>(
            $"/api/v1/inventory/alerts?warehouseId={seed.SecondWarehouse.Id}");
        Assert.NotNull(after);
        var alert = Assert.Single(after.Items);
        Assert.Equal(seed.BoundaryRule.Id, alert.RuleId);
        Assert.Equal(45.0000m, alert.OnHandQuantity);
        Assert.Equal(45.0001m, alert.MinimumQuantity);
    }

    [Fact]
    public async Task ListAlerts_SupportsFiltersAndPaging()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory);

        var byCode = await client.GetFromJsonAsync<AlertPagedApiResult>(
            "/api/v1/inventory/alerts?keyword=POWDER");
        Assert.NotNull(byCode);
        var codeAlert = Assert.Single(byCode.Items);
        Assert.Equal("MAT-POWDER-001", codeAlert.MaterialCode);

        var byName = await client.GetFromJsonAsync<AlertPagedApiResult>(
            "/api/v1/inventory/alerts?keyword=%E6%A0%91%E8%84%82");
        Assert.NotNull(byName);
        var nameAlert = Assert.Single(byName.Items);
        Assert.Equal("MAT-RESIN-001", nameAlert.MaterialCode);

        var byScope = await client.GetFromJsonAsync<AlertPagedApiResult>(
            $"/api/v1/inventory/alerts?warehouseId={seed.MainWarehouse.Id}&materialId={seed.Resin.Id}");
        Assert.NotNull(byScope);
        var scopeAlert = Assert.Single(byScope.Items);
        Assert.Equal(seed.ResinRule.Id, scopeAlert.RuleId);

        var unknown = await client.GetFromJsonAsync<AlertPagedApiResult>(
            $"/api/v1/inventory/alerts?warehouseId={Guid.NewGuid()}");
        Assert.NotNull(unknown);
        Assert.Empty(unknown.Items);

        var firstPage = await client.GetFromJsonAsync<AlertPagedApiResult>(
            "/api/v1/inventory/alerts?page=1&pageSize=1");
        Assert.NotNull(firstPage);
        Assert.Equal("MAT-POWDER-001", firstPage.Items[0].MaterialCode);
        Assert.Equal(1, firstPage.Page);
        Assert.Equal(1, firstPage.PageSize);
        Assert.Equal(2, firstPage.TotalCount);

        var secondPage = await client.GetFromJsonAsync<AlertPagedApiResult>(
            "/api/v1/inventory/alerts?page=2&pageSize=1");
        Assert.NotNull(secondPage);
        Assert.Equal("MAT-RESIN-001", secondPage.Items[0].MaterialCode);

        var clamped = await client.GetFromJsonAsync<AlertPagedApiResult>(
            "/api/v1/inventory/alerts?page=0&pageSize=0");
        Assert.NotNull(clamped);
        Assert.Equal(1, clamped.Page);
        Assert.Equal(10, clamped.PageSize);
        Assert.Equal(2, clamped.Items.Count);

        var maxPageSize = await client.GetFromJsonAsync<AlertPagedApiResult>(
            "/api/v1/inventory/alerts?pageSize=500");
        Assert.NotNull(maxPageSize);
        Assert.Equal(100, maxPageSize.PageSize);
    }

    [Fact]
    public async Task ListAlerts_InvalidQueryParameters_ReturnBadRequest()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();

        var invalidWarehouseId = await client.GetAsync("/api/v1/inventory/alerts?warehouseId=not-a-guid");
        Assert.Equal(HttpStatusCode.BadRequest, invalidWarehouseId.StatusCode);
        Assert.Equal("application/problem+json", invalidWarehouseId.Content.Headers.ContentType?.MediaType);

        var invalidMaterialId = await client.GetAsync("/api/v1/inventory/alerts?materialId=not-a-guid");
        Assert.Equal(HttpStatusCode.BadRequest, invalidMaterialId.StatusCode);

        var invalidPage = await client.GetAsync("/api/v1/inventory/alerts?page=abc");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
    }

    [Fact]
    public async Task ListAlertRules_ReturnsRulesWithFilters()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory);

        var all = await client.GetFromJsonAsync<RulePagedApiResult>("/api/v1/inventory/alert-rules");
        Assert.NotNull(all);
        Assert.Equal(4, all.TotalCount);

        // 稳定排序：仓库编码升序，再按物料编码升序。
        Assert.Collection(all.Items,
            rule =>
            {
                Assert.Equal(seed.PowderRule.Id, rule.Id);
                Assert.Equal("WH-A", rule.WarehouseCode);
                Assert.Equal("第一仓库", rule.WarehouseName);
                Assert.Equal(seed.Powder.Id, rule.MaterialId);
                Assert.Equal("MAT-POWDER-001", rule.MaterialCode);
                Assert.Equal("钛合金粉末", rule.MaterialName);
                Assert.Equal("kg", rule.MaterialUnit);
                Assert.Equal(10.0000m, rule.MinimumQuantity);
                Assert.True(rule.IsEnabled);
            },
            rule => Assert.Equal(seed.ResinRule.Id, rule.Id),
            rule => Assert.Equal(seed.DisabledRule.Id, rule.Id),
            rule => Assert.Equal(seed.BoundaryRule.Id, rule.Id));

        var disabledOnly = await client.GetFromJsonAsync<RulePagedApiResult>(
            "/api/v1/inventory/alert-rules?isEnabled=false");
        Assert.NotNull(disabledOnly);
        var disabledRule = Assert.Single(disabledOnly.Items);
        Assert.Equal(seed.DisabledRule.Id, disabledRule.Id);
        Assert.False(disabledRule.IsEnabled);

        var byKeyword = await client.GetFromJsonAsync<RulePagedApiResult>(
            "/api/v1/inventory/alert-rules?keyword=POWDER");
        Assert.NotNull(byKeyword);
        Assert.Equal(2, byKeyword.TotalCount);
        Assert.All(byKeyword.Items, rule => Assert.Equal(seed.Powder.Id, rule.MaterialId));

        var byWarehouse = await client.GetFromJsonAsync<RulePagedApiResult>(
            $"/api/v1/inventory/alert-rules?warehouseId={seed.SecondWarehouse.Id}");
        Assert.NotNull(byWarehouse);
        Assert.Equal(2, byWarehouse.TotalCount);
        Assert.All(byWarehouse.Items, rule => Assert.Equal(seed.SecondWarehouse.Id, rule.WarehouseId));
    }

    [Fact]
    public async Task CreateRule_ValidRequest_ReturnsCreatedRuleWithLocation()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        await SeedAsync(factory);

        // 新仓库 × 新物料，避免与播种的规则重复。
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
            context.Warehouses.Add(CreateWarehouse("WH-C", "第三仓库"));
            context.Materials.Add(CreateMaterial("MAT-FILAMENT-001", "PLA 线材"));
            await context.SaveChangesAsync();
        }

        var warehouseId = await RequireWarehouseIdAsync(factory, "WH-C");
        var materialId = await RequireMaterialIdAsync(factory, "MAT-FILAMENT-001");

        var response = await client.PostAsJsonAsync(
            "/api/v1/inventory/alert-rules",
            new
            {
                warehouseId,
                materialId,
                minimumQuantity = 25.5000m
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var created = await response.Content.ReadFromJsonAsync<InventoryAlertRuleApiModel>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal(warehouseId, created.WarehouseId);
        Assert.Equal(materialId, created.MaterialId);
        Assert.Equal("WH-C", created.WarehouseCode);
        Assert.Equal("第三仓库", created.WarehouseName);
        Assert.Equal("MAT-FILAMENT-001", created.MaterialCode);
        Assert.Equal("PLA 线材", created.MaterialName);
        Assert.Equal("kg", created.MaterialUnit);
        Assert.Equal(25.5000m, created.MinimumQuantity);
        Assert.True(created.IsEnabled);

        // Location 指向 GET by id，且可查询到同一条规则。
        var byId = await client.GetFromJsonAsync<InventoryAlertRuleApiModel>(
            $"/api/v1/inventory/alert-rules/{created.Id}");
        Assert.NotNull(byId);
        Assert.Equal(created.Id, byId.Id);
        Assert.Equal(25.5000m, byId.MinimumQuantity);

        // 列表中也能查到（共 5 条）。
        var list = await client.GetFromJsonAsync<RulePagedApiResult>("/api/v1/inventory/alert-rules");
        Assert.NotNull(list);
        Assert.Equal(5, list.TotalCount);

        // 新规则阈值 25.5，而该组合没有余额记录 → 不产生预警结果。
        var alerts = await client.GetFromJsonAsync<AlertPagedApiResult>(
            $"/api/v1/inventory/alerts?warehouseId={warehouseId}");
        Assert.NotNull(alerts);
        Assert.Empty(alerts.Items);
    }

    [Fact]
    public async Task CreateRule_DuplicateScope_ReturnsConflict()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/inventory/alert-rules",
            new
            {
                warehouseId = seed.MainWarehouse.Id,
                materialId = seed.Powder.Id,
                minimumQuantity = 99m
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemApiResult>();
        Assert.NotNull(problem);
        Assert.Contains("已存在预警规则", problem.Detail);
    }

    [Fact]
    public async Task CreateRule_InvalidRequest_ReturnsValidationProblem()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();

        var negativeThreshold = await client.PostAsJsonAsync(
            "/api/v1/inventory/alert-rules",
            new { warehouseId = Guid.Empty, materialId = Guid.Empty, minimumQuantity = -1m });

        Assert.Equal(HttpStatusCode.BadRequest, negativeThreshold.StatusCode);
        Assert.Equal("application/problem+json", negativeThreshold.Content.Headers.ContentType?.MediaType);

        var problem = await negativeThreshold.Content.ReadFromJsonAsync<ValidationProblemApiResult>();
        Assert.NotNull(problem);
        Assert.Contains("warehouseId", problem.Errors.Keys);
        Assert.Contains("materialId", problem.Errors.Keys);
        Assert.Contains("minimumQuantity", problem.Errors.Keys);

        // 超过 4 位小数同样校验失败。
        var tooManyDecimals = await client.PostAsJsonAsync(
            "/api/v1/inventory/alert-rules",
            new { warehouseId = Guid.NewGuid(), materialId = Guid.NewGuid(), minimumQuantity = 1.23456m });
        Assert.Equal(HttpStatusCode.BadRequest, tooManyDecimals.StatusCode);
    }

    [Fact]
    public async Task CreateRule_UnknownWarehouseOrMaterial_ReturnsNotFound()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory);

        var unknownWarehouse = await client.PostAsJsonAsync(
            "/api/v1/inventory/alert-rules",
            new { warehouseId = Guid.NewGuid(), materialId = seed.Powder.Id, minimumQuantity = 10m });
        Assert.Equal(HttpStatusCode.NotFound, unknownWarehouse.StatusCode);

        var unknownMaterial = await client.PostAsJsonAsync(
            "/api/v1/inventory/alert-rules",
            new { warehouseId = seed.MainWarehouse.Id, materialId = Guid.NewGuid(), minimumQuantity = 10m });
        Assert.Equal(HttpStatusCode.NotFound, unknownMaterial.StatusCode);
    }

    [Fact]
    public async Task GetRule_UnknownId_ReturnsNotFound()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/inventory/alert-rules/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRule_EditsThresholdTogglesEnablement_AndAlertsReflectChanges()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory);

        var before = await client.GetFromJsonAsync<AlertPagedApiResult>("/api/v1/inventory/alerts");
        Assert.NotNull(before);
        Assert.Equal(2, before.TotalCount);

        // 1) 停用的规则启用后立即参与预警（WH-B 粉末：1 < 5）。
        var enable = await client.PutAsJsonAsync(
            $"/api/v1/inventory/alert-rules/{seed.DisabledRule.Id}",
            new
            {
                warehouseId = seed.SecondWarehouse.Id,
                materialId = seed.Powder.Id,
                minimumQuantity = 5.0000m,
                isEnabled = true
            });
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        var enabledRule = await enable.Content.ReadFromJsonAsync<InventoryAlertRuleApiModel>();
        Assert.NotNull(enabledRule);
        Assert.True(enabledRule.IsEnabled);

        var afterEnable = await client.GetFromJsonAsync<AlertPagedApiResult>("/api/v1/inventory/alerts");
        Assert.NotNull(afterEnable);
        Assert.Equal(3, afterEnable.TotalCount);
        Assert.Contains(afterEnable.Items, alert =>
            alert.RuleId == seed.DisabledRule.Id
            && alert.OnHandQuantity == 1.0000m
            && alert.MinimumQuantity == 5.0000m);

        // 2) 阈值降到余额之下后不再预警（WH-A 粉末：8.25 < 8 不成立）。
        var lower = await client.PutAsJsonAsync(
            $"/api/v1/inventory/alert-rules/{seed.PowderRule.Id}",
            new
            {
                warehouseId = seed.MainWarehouse.Id,
                materialId = seed.Powder.Id,
                minimumQuantity = 8.0000m,
                isEnabled = true
            });
        Assert.Equal(HttpStatusCode.OK, lower.StatusCode);

        var afterLower = await client.GetFromJsonAsync<AlertPagedApiResult>("/api/v1/inventory/alerts");
        Assert.NotNull(afterLower);
        Assert.Equal(2, afterLower.TotalCount);
        Assert.DoesNotContain(afterLower.Items, alert => alert.RuleId == seed.PowderRule.Id);

        // 3) 停用规则后预警即时消失。
        var disable = await client.PutAsJsonAsync(
            $"/api/v1/inventory/alert-rules/{seed.ResinRule.Id}",
            new
            {
                warehouseId = seed.MainWarehouse.Id,
                materialId = seed.Resin.Id,
                minimumQuantity = 150.0000m,
                isEnabled = false
            });
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);

        var afterDisable = await client.GetFromJsonAsync<AlertPagedApiResult>("/api/v1/inventory/alerts");
        Assert.NotNull(afterDisable);
        var remaining = Assert.Single(afterDisable.Items);
        Assert.Equal(seed.DisabledRule.Id, remaining.RuleId);

        // 4) 规则列表同步反映启停状态。
        var rules = await client.GetFromJsonAsync<RulePagedApiResult>(
            "/api/v1/inventory/alert-rules?isEnabled=false");
        Assert.NotNull(rules);
        var disabledRule = Assert.Single(rules.Items);
        Assert.Equal(seed.ResinRule.Id, disabledRule.Id);
    }

    [Fact]
    public async Task UpdateRule_ChangeScopeToExistingRule_ReturnsConflict()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/inventory/alert-rules/{seed.PowderRule.Id}",
            new
            {
                warehouseId = seed.MainWarehouse.Id,
                materialId = seed.Resin.Id,
                minimumQuantity = 10m,
                isEnabled = true
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemApiResult>();
        Assert.NotNull(problem);
        Assert.Contains("已存在预警规则", problem.Detail);
    }

    [Fact]
    public async Task UpdateRule_UnknownId_ReturnsNotFound()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/inventory/alert-rules/{Guid.NewGuid()}",
            new
            {
                warehouseId = seed.MainWarehouse.Id,
                materialId = seed.Powder.Id,
                minimumQuantity = 10m,
                isEnabled = true
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRule_InvalidRequest_ReturnsValidationProblem()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/inventory/alert-rules/{seed.PowderRule.Id}",
            new
            {
                warehouseId = seed.MainWarehouse.Id,
                materialId = seed.Powder.Id,
                minimumQuantity = -3m,
                isEnabled = true
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemApiResult>();
        Assert.NotNull(problem);
        Assert.Contains("minimumQuantity", problem.Errors.Keys);

        // 校验失败时不落库，预警结果保持原样。
        var alerts = await client.GetFromJsonAsync<AlertPagedApiResult>("/api/v1/inventory/alerts");
        Assert.NotNull(alerts);
        Assert.Equal(2, alerts.TotalCount);
        Assert.Equal(
            10.0000m,
            alerts.Items.Single(alert => alert.RuleId == seed.PowderRule.Id).MinimumQuantity);
    }

    [Fact]
    public async Task UpdateRule_UnknownWarehouse_ReturnsNotFound()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/inventory/alert-rules/{seed.PowderRule.Id}",
            new
            {
                warehouseId = Guid.NewGuid(),
                materialId = seed.Powder.Id,
                minimumQuantity = 10m,
                isEnabled = true
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<Guid> RequireWarehouseIdAsync(InventoryApiFactory factory, string code)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var warehouse = await context.Warehouses.AsNoTracking().SingleAsync(item => item.Code == code);
        return warehouse.Id;
    }

    private static async Task<Guid> RequireMaterialIdAsync(InventoryApiFactory factory, string code)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var material = await context.Materials.AsNoTracking().SingleAsync(item => item.Code == code);
        return material.Id;
    }

    private sealed record InventoryAlertRuleApiModel(
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

    private sealed record InventoryAlertApiModel(
        Guid WarehouseId,
        string WarehouseCode,
        string WarehouseName,
        Guid MaterialId,
        string MaterialCode,
        string MaterialName,
        string MaterialUnit,
        decimal OnHandQuantity,
        Guid RuleId,
        decimal MinimumQuantity,
        string AlertType);

    private sealed record RulePagedApiResult(
        List<InventoryAlertRuleApiModel> Items,
        int Page,
        int PageSize,
        int TotalCount,
        int TotalPages);

    private sealed record AlertPagedApiResult(
        List<InventoryAlertApiModel> Items,
        int Page,
        int PageSize,
        int TotalCount,
        int TotalPages);

    private sealed record ValidationProblemApiResult(
        IReadOnlyDictionary<string, string[]> Errors,
        string Title);

    private sealed record ProblemApiResult(string Title, string Detail, int Status);
}
