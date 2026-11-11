using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ThreeDErp.Domain.Customers;
using ThreeDErp.Domain.Inventory;
using ThreeDErp.Domain.Materials;
using ThreeDErp.Domain.Suppliers;
using ThreeDErp.Domain.Warehouses;
using ThreeDErp.Infrastructure.Persistence;

namespace ThreeDErp.IntegrationTests.Inventory;

/// <summary>
/// /api/v1/inventory/balances 集成测试：覆盖空库、过滤（仓库 / 物料 / 关键字）、
/// 分页、无效参数（越界值收敛、非法 Guid / 数字 400）。数据通过 DbContext 直接播种，
/// 同时验证库存三张表的实体映射可在真实管道中持久化。
/// </summary>
public sealed class InventoryEndpointsTests
{
    private sealed class SeedData
    {
        public Warehouse MainWarehouse { get; init; } = null!;

        public Warehouse SecondWarehouse { get; init; } = null!;

        public Material Powder { get; init; } = null!;

        public Material Resin { get; init; } = null!;
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
    /// 播种主数据与库存：2 仓库 × 2 物料共 3 条余额（WH-A 两种物料、WH-B 仅树脂），
    /// 另写入流水（含供应商 / 客户外键）与预警规则，验证完整模型可持久化。
    /// </summary>
    private static async Task<SeedData> SeedAsync(InventoryApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        await context.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        var mainWarehouse = CreateWarehouse("WH-A", "第一仓库");
        var secondWarehouse = CreateWarehouse("WH-B", "第二仓库");
        var powder = CreateMaterial("MAT-POWDER-001", "钛合金粉末");
        var resin = CreateMaterial("MAT-RESIN-001", "光敏树脂");
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Code = "SUP-001",
            Name = "树脂供应商",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            Code = "CUS-001",
            Name = "打印服务中心",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        context.Warehouses.AddRange(mainWarehouse, secondWarehouse);
        context.Materials.AddRange(powder, resin);
        context.Suppliers.Add(supplier);
        context.Customers.Add(customer);

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
            });

        context.InventoryMovements.AddRange(
            new InventoryMovement
            {
                Id = Guid.NewGuid(),
                MovementType = InventoryMovementType.In,
                DocumentNo = "PO-2026-0001",
                WarehouseId = mainWarehouse.Id,
                MaterialId = resin.Id,
                Quantity = 150.0000m,
                SupplierId = supplier.Id,
                OccurredAtUtc = now.AddDays(-2),
                Remark = "采购收货"
            },
            new InventoryMovement
            {
                Id = Guid.NewGuid(),
                MovementType = InventoryMovementType.Out,
                DocumentNo = "SO-2026-0001",
                WarehouseId = mainWarehouse.Id,
                MaterialId = resin.Id,
                Quantity = 29.5000m,
                CustomerId = customer.Id,
                OccurredAtUtc = now.AddDays(-1)
            },
            new InventoryMovement
            {
                Id = Guid.NewGuid(),
                MovementType = InventoryMovementType.Adjustment,
                DocumentNo = "ADJ-2026-0001",
                WarehouseId = mainWarehouse.Id,
                MaterialId = powder.Id,
                Quantity = 0.2500m,
                OccurredAtUtc = now,
                Remark = "盘点盘亏"
            });

        context.InventoryAlertRules.Add(new InventoryAlertRule
        {
            Id = Guid.NewGuid(),
            WarehouseId = mainWarehouse.Id,
            MaterialId = powder.Id,
            MinimumQuantity = 10.0000m,
            IsEnabled = true
        });

        await context.SaveChangesAsync();

        return new SeedData
        {
            MainWarehouse = mainWarehouse,
            SecondWarehouse = secondWarehouse,
            Powder = powder,
            Resin = resin
        };
    }

    [Fact]
    public async Task ListBalances_EmptyDatabase_ReturnsEmptyPage()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<PagedApiResult>("/api/v1/inventory/balances");

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public async Task ListBalances_ReturnsSummariesWithStableOrdering()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory);

        var result = await client.GetFromJsonAsync<PagedApiResult>("/api/v1/inventory/balances");

        Assert.NotNull(result);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Items.Count);

        // 稳定排序：仓库编码升序，再按物料编码升序。
        Assert.Collection(result.Items,
            item =>
            {
                Assert.Equal(seed.MainWarehouse.Id, item.WarehouseId);
                Assert.Equal("WH-A", item.WarehouseCode);
                Assert.Equal("第一仓库", item.WarehouseName);
                Assert.Equal(seed.Powder.Id, item.MaterialId);
                Assert.Equal("MAT-POWDER-001", item.MaterialCode);
                Assert.Equal("钛合金粉末", item.MaterialName);
                Assert.Equal("kg", item.MaterialUnit);
                Assert.Equal(8.2500m, item.OnHandQuantity);
            },
            item =>
            {
                Assert.Equal("WH-A", item.WarehouseCode);
                Assert.Equal("MAT-RESIN-001", item.MaterialCode);
                Assert.Equal(120.5000m, item.OnHandQuantity);
            },
            item =>
            {
                Assert.Equal("WH-B", item.WarehouseCode);
                Assert.Equal("第二仓库", item.WarehouseName);
                Assert.Equal("MAT-RESIN-001", item.MaterialCode);
                Assert.Equal(seed.Resin.Id, item.MaterialId);
                Assert.Equal(45.0000m, item.OnHandQuantity);
            });
    }

    [Fact]
    public async Task ListBalances_FiltersByWarehouseIdAndMaterialId()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory);

        var byWarehouse = await client.GetFromJsonAsync<PagedApiResult>(
            $"/api/v1/inventory/balances?warehouseId={seed.MainWarehouse.Id}");
        Assert.NotNull(byWarehouse);
        Assert.Equal(2, byWarehouse.TotalCount);
        Assert.All(byWarehouse.Items, item => Assert.Equal(seed.MainWarehouse.Id, item.WarehouseId));

        var byMaterial = await client.GetFromJsonAsync<PagedApiResult>(
            $"/api/v1/inventory/balances?materialId={seed.Resin.Id}");
        Assert.NotNull(byMaterial);
        Assert.Equal(2, byMaterial.TotalCount);
        Assert.All(byMaterial.Items, item => Assert.Equal(seed.Resin.Id, item.MaterialId));

        var byBoth = await client.GetFromJsonAsync<PagedApiResult>(
            $"/api/v1/inventory/balances?warehouseId={seed.SecondWarehouse.Id}&materialId={seed.Resin.Id}");
        Assert.NotNull(byBoth);
        var bothItem = Assert.Single(byBoth.Items);
        Assert.Equal(45.0000m, bothItem.OnHandQuantity);

        // 组合过滤不匹配任何余额时返回空结果而不是错误。
        var noMatch = await client.GetFromJsonAsync<PagedApiResult>(
            $"/api/v1/inventory/balances?warehouseId={seed.SecondWarehouse.Id}&materialId={seed.Powder.Id}");
        Assert.NotNull(noMatch);
        Assert.Empty(noMatch.Items);
        Assert.Equal(0, noMatch.TotalCount);
    }

    [Fact]
    public async Task ListBalances_FiltersByMaterialKeyword()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        await SeedAsync(factory);

        var byCode = await client.GetFromJsonAsync<PagedApiResult>(
            "/api/v1/inventory/balances?keyword=POWDER");
        Assert.NotNull(byCode);
        var codeItem = Assert.Single(byCode.Items);
        Assert.Equal("MAT-POWDER-001", codeItem.MaterialCode);
        Assert.Equal(8.2500m, codeItem.OnHandQuantity);

        var byName = await client.GetFromJsonAsync<PagedApiResult>(
            "/api/v1/inventory/balances?keyword=%E6%A0%91%E8%84%82");
        Assert.NotNull(byName);
        Assert.Equal(2, byName.TotalCount);
        Assert.All(byName.Items, item => Assert.Equal("MAT-RESIN-001", item.MaterialCode));

        // 关键字可与分页组合。
        var combined = await client.GetFromJsonAsync<PagedApiResult>(
            "/api/v1/inventory/balances?keyword=%E6%A0%91%E8%84%82&pageSize=1");
        Assert.NotNull(combined);
        Assert.Single(combined.Items);
        Assert.Equal(2, combined.TotalCount);
    }

    [Fact]
    public async Task ListBalances_AppliesPaging()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        await SeedAsync(factory);

        var firstPage = await client.GetFromJsonAsync<PagedApiResult>(
            "/api/v1/inventory/balances?page=1&pageSize=2");
        Assert.NotNull(firstPage);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Equal(1, firstPage.Page);
        Assert.Equal(2, firstPage.PageSize);
        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Collection(firstPage.Items,
            item => Assert.Equal("MAT-POWDER-001", item.MaterialCode),
            item => Assert.Equal("MAT-RESIN-001", item.MaterialCode));

        var secondPage = await client.GetFromJsonAsync<PagedApiResult>(
            "/api/v1/inventory/balances?page=2&pageSize=2");
        Assert.NotNull(secondPage);
        var lastItem = Assert.Single(secondPage.Items);
        Assert.Equal("WH-B", lastItem.WarehouseCode);
        Assert.Equal(3, secondPage.TotalCount);

        // 超出范围的页码返回空页而不是错误。
        var beyond = await client.GetFromJsonAsync<PagedApiResult>(
            "/api/v1/inventory/balances?page=99&pageSize=2");
        Assert.NotNull(beyond);
        Assert.Empty(beyond.Items);
        Assert.Equal(3, beyond.TotalCount);
    }

    [Fact]
    public async Task ListBalances_UnknownWarehouseId_ReturnsEmptyResult()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        await SeedAsync(factory);

        var result = await client.GetFromJsonAsync<PagedApiResult>(
            $"/api/v1/inventory/balances?warehouseId={Guid.NewGuid()}");

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task ListBalances_OutOfRangePagingParameters_AreClamped()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        await SeedAsync(factory);

        var clamped = await client.GetFromJsonAsync<PagedApiResult>(
            "/api/v1/inventory/balances?page=0&pageSize=0");
        Assert.NotNull(clamped);
        Assert.Equal(1, clamped.Page);
        Assert.Equal(10, clamped.PageSize);
        Assert.Equal(3, clamped.TotalCount);
        Assert.Equal(3, clamped.Items.Count);

        var maxPageSize = await client.GetFromJsonAsync<PagedApiResult>(
            "/api/v1/inventory/balances?pageSize=500");
        Assert.NotNull(maxPageSize);
        Assert.Equal(100, maxPageSize.PageSize);
    }

    [Fact]
    public async Task ListBalances_InvalidParameters_ReturnValidationProblem()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();

        var invalidWarehouseId = await client.GetAsync("/api/v1/inventory/balances?warehouseId=not-a-guid");
        Assert.Equal(HttpStatusCode.BadRequest, invalidWarehouseId.StatusCode);
        Assert.Equal("application/problem+json", invalidWarehouseId.Content.Headers.ContentType?.MediaType);

        var invalidMaterialId = await client.GetAsync("/api/v1/inventory/balances?materialId=not-a-guid");
        Assert.Equal(HttpStatusCode.BadRequest, invalidMaterialId.StatusCode);

        var invalidPage = await client.GetAsync("/api/v1/inventory/balances?page=abc");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);

        var invalidPageSize = await client.GetAsync("/api/v1/inventory/balances?pageSize=abc");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPageSize.StatusCode);
    }

    private sealed record InventoryBalanceApiModel(
        Guid Id,
        Guid WarehouseId,
        string WarehouseCode,
        string WarehouseName,
        Guid MaterialId,
        string MaterialCode,
        string MaterialName,
        string MaterialUnit,
        decimal OnHandQuantity);

    private sealed record PagedApiResult(
        List<InventoryBalanceApiModel> Items,
        int Page,
        int PageSize,
        int TotalCount,
        int TotalPages);
}
