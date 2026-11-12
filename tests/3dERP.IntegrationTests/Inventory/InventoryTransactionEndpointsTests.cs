using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ThreeDErp.Domain.Customers;
using ThreeDErp.Domain.Materials;
using ThreeDErp.Domain.Suppliers;
using ThreeDErp.Domain.Warehouses;
using ThreeDErp.Infrastructure.Persistence;

namespace ThreeDErp.IntegrationTests.Inventory;

/// <summary>
/// POST /api/v1/inventory/inbound | /outbound 集成测试：覆盖快乐路径（建余额 / 加减余额）、
/// 禁止负库存 409、重复单据号 409（同类型拒绝 / 跨类型放行）、
/// 校验失败 400 与主数据未知 404。数据通过 DbContext 直接播种，
/// 结果同时通过 GET /balances 与 DbContext 断言。
/// </summary>
public sealed class InventoryTransactionEndpointsTests
{
    private sealed record Seed(
        Guid WarehouseId,
        Guid MaterialId,
        Guid SupplierId,
        Guid CustomerId,
        decimal InitialQuantity);

    /// <summary>
    /// 播种一套主数据（1 仓库 / 1 物料 / 1 供应商 / 1 客户），initialQuantity 非负时
    /// 额外创建一条余额；返回全部 Id 供请求构造使用。
    /// </summary>
    private static async Task<Seed> SeedAsync(InventoryApiFactory factory, decimal? initialQuantity)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        await context.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            Code = $"WH-{Guid.NewGuid():N}"[..14].ToUpperInvariant(),
            Name = "事务测试仓库",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var material = new Material
        {
            Id = Guid.NewGuid(),
            Code = $"MAT-{Guid.NewGuid():N}"[..14].ToUpperInvariant(),
            Name = "光敏树脂",
            Unit = "kg",
            MaterialType = "树脂",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Code = $"SUP-{Guid.NewGuid():N}"[..14].ToUpperInvariant(),
            Name = "事务测试供应商",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            Code = $"CUS-{Guid.NewGuid():N}"[..14].ToUpperInvariant(),
            Name = "事务测试客户",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        context.AddRange(warehouse, material, supplier, customer);

        if (initialQuantity is not null)
        {
            context.InventoryBalances.Add(new ThreeDErp.Domain.Inventory.InventoryBalance
            {
                Id = Guid.NewGuid(),
                WarehouseId = warehouse.Id,
                MaterialId = material.Id,
                OnHandQuantity = initialQuantity.Value
            });
        }

        await context.SaveChangesAsync();

        return new Seed(warehouse.Id, material.Id, supplier.Id, customer.Id, initialQuantity ?? 0m);
    }

    private static object BuildPayload(
        Seed seed,
        decimal quantity,
        string documentNo,
        Guid? supplierId = null,
        Guid? customerId = null,
        string? note = null) => new
    {
        warehouseId = seed.WarehouseId,
        materialId = seed.MaterialId,
        quantity,
        documentNo,
        supplierId,
        customerId,
        note
    };

    private static async Task<decimal> GetBalanceAsync(InventoryApiFactory factory, Seed seed)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var balance = await context.InventoryBalances.SingleAsync(
            item => item.WarehouseId == seed.WarehouseId && item.MaterialId == seed.MaterialId);
        return balance.OnHandQuantity;
    }

    private static async Task<int> CountMovementsAsync(InventoryApiFactory factory, Seed seed)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        return await context.InventoryMovements.CountAsync(
            movement => movement.WarehouseId == seed.WarehouseId && movement.MaterialId == seed.MaterialId);
    }

    [Fact]
    public async Task PostInbound_NewWarehouseMaterial_CreatesBalanceMovementAndReturns201()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory, initialQuantity: null);

        var response = await client.PostAsJsonAsync(
            "/api/v1/inventory/inbound",
            BuildPayload(seed, 50.5000m, "PO-2026-0100", supplierId: seed.SupplierId, note: "采购收货"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var movement = await response.Content.ReadFromJsonAsync<MovementApiModel>();
        Assert.NotNull(movement);
        Assert.NotEqual(Guid.Empty, movement.Id);
        // Results.Created 写入相对 URI 的 Location 头，指向流水资源。
        Assert.Equal($"/api/v1/inventory/movements/{movement.Id}", response.Headers.Location?.ToString());
        Assert.Equal("In", movement.MovementType);
        Assert.Equal("PO-2026-0100", movement.DocumentNo);
        Assert.Equal(seed.WarehouseId, movement.WarehouseId);
        Assert.Equal(seed.MaterialId, movement.MaterialId);
        Assert.Equal(50.5000m, movement.Quantity);
        Assert.Equal(seed.SupplierId, movement.SupplierId);
        Assert.Null(movement.CustomerId);
        Assert.Equal("采购收货", movement.Note);
        Assert.Equal(50.5000m, movement.OnHandQuantity);

        // 流水落库 + 余额可由查询端点看到。
        Assert.Equal(1, await CountMovementsAsync(factory, seed));
        Assert.Equal(50.5000m, await GetBalanceAsync(factory, seed));
        var balances = await client.GetFromJsonAsync<PagedApiResult>($"/api/v1/inventory/balances?warehouseId={seed.WarehouseId}");
        Assert.NotNull(balances);
        var item = Assert.Single(balances.Items);
        Assert.Equal(50.5000m, item.OnHandQuantity);
    }

    [Fact]
    public async Task PostInbound_ExistingBalance_AccumulatesQuantity()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory, initialQuantity: 10.0000m);

        var first = await client.PostAsJsonAsync(
            "/api/v1/inventory/inbound", BuildPayload(seed, 5.2500m, "PO-2026-0101"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync(
            "/api/v1/inventory/inbound", BuildPayload(seed, 2.7500m, "PO-2026-0102"));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var movements = await second.Content.ReadFromJsonAsync<MovementApiModel>();
        Assert.NotNull(movements);
        Assert.Equal(18.0000m, movements.OnHandQuantity);

        Assert.Equal(2, await CountMovementsAsync(factory, seed));
        Assert.Equal(18.0000m, await GetBalanceAsync(factory, seed));
    }

    [Fact]
    public async Task PostOutbound_DecrementsBalanceAndWritesMovement()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory, initialQuantity: 100.0000m);

        var response = await client.PostAsJsonAsync(
            "/api/v1/inventory/outbound",
            BuildPayload(seed, 30.2500m, "SO-2026-0100", customerId: seed.CustomerId, note: "销售发货"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var movement = await response.Content.ReadFromJsonAsync<MovementApiModel>();
        Assert.NotNull(movement);
        Assert.Equal("Out", movement.MovementType);
        Assert.Equal(30.2500m, movement.Quantity);
        Assert.Equal(seed.CustomerId, movement.CustomerId);
        Assert.Null(movement.SupplierId);
        Assert.Equal("销售发货", movement.Note);
        Assert.Equal(69.7500m, movement.OnHandQuantity);

        Assert.Equal(69.7500m, await GetBalanceAsync(factory, seed));
        Assert.Equal(1, await CountMovementsAsync(factory, seed));
    }

    [Fact]
    public async Task PostOutbound_ExactBalance_IsAllowedDownToZero()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory, initialQuantity: 25.0000m);

        var response = await client.PostAsJsonAsync(
            "/api/v1/inventory/outbound",
            BuildPayload(seed, 25.0000m, "SO-2026-0101", customerId: seed.CustomerId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var movement = await response.Content.ReadFromJsonAsync<MovementApiModel>();
        Assert.NotNull(movement);
        Assert.Equal(0.0000m, movement.OnHandQuantity);
        Assert.Equal(0.0000m, await GetBalanceAsync(factory, seed));
    }

    [Fact]
    public async Task PostOutbound_InsufficientStock_ReturnsConflictAndKeepsBalance()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory, initialQuantity: 10.0000m);

        var response = await client.PostAsJsonAsync(
            "/api/v1/inventory/outbound",
            BuildPayload(seed, 10.5000m, "SO-2026-0102", customerId: seed.CustomerId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        // 失败的事务不留下任何流水，余额保持不变。
        Assert.Equal(0, await CountMovementsAsync(factory, seed));
        Assert.Equal(10.0000m, await GetBalanceAsync(factory, seed));
    }

    [Fact]
    public async Task PostOutbound_NoBalanceAtAll_ReturnsConflict()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory, initialQuantity: null);

        var response = await client.PostAsJsonAsync(
            "/api/v1/inventory/outbound",
            BuildPayload(seed, 1.0000m, "SO-2026-0103", customerId: seed.CustomerId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await CountMovementsAsync(factory, seed));
    }

    [Fact]
    public async Task PostInbound_DuplicateDocumentNo_ReturnsConflictWithoutSecondMovement()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory, initialQuantity: null);

        var first = await client.PostAsJsonAsync(
            "/api/v1/inventory/inbound", BuildPayload(seed, 5.0000m, "PO-2026-0104"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var duplicate = await client.PostAsJsonAsync(
            "/api/v1/inventory/inbound", BuildPayload(seed, 5.0000m, "PO-2026-0104"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("application/problem+json", duplicate.Content.Headers.ContentType?.MediaType);

        Assert.Equal(1, await CountMovementsAsync(factory, seed));
        Assert.Equal(5.0000m, await GetBalanceAsync(factory, seed));
    }

    [Fact]
    public async Task PostTransactions_SameDocumentNoAcrossTypes_IsAllowed()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory, initialQuantity: 20.0000m);

        // 出库与入库使用相同单据号：唯一索引为 (MovementType, DocumentNo)，跨类型不冲突。
        var outbound = await client.PostAsJsonAsync(
            "/api/v1/inventory/outbound",
            BuildPayload(seed, 5.0000m, "DOC-2026-0001", customerId: seed.CustomerId));
        Assert.Equal(HttpStatusCode.Created, outbound.StatusCode);

        var inbound = await client.PostAsJsonAsync(
            "/api/v1/inventory/inbound",
            BuildPayload(seed, 5.0000m, "DOC-2026-0001", supplierId: seed.SupplierId));
        Assert.Equal(HttpStatusCode.Created, inbound.StatusCode);

        Assert.Equal(2, await CountMovementsAsync(factory, seed));
        Assert.Equal(20.0000m, await GetBalanceAsync(factory, seed));
    }

    [Fact]
    public async Task PostTransactions_DuplicateDocumentNoOnOutbound_ReturnsConflict()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory, initialQuantity: 100.0000m);

        var first = await client.PostAsJsonAsync(
            "/api/v1/inventory/outbound",
            BuildPayload(seed, 5.0000m, "SO-2026-0105", customerId: seed.CustomerId));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var duplicate = await client.PostAsJsonAsync(
            "/api/v1/inventory/outbound",
            BuildPayload(seed, 5.0000m, "SO-2026-0105", customerId: seed.CustomerId));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(1, await CountMovementsAsync(factory, seed));
        Assert.Equal(95.0000m, await GetBalanceAsync(factory, seed));
    }

    [Fact]
    public async Task PostTransactions_InvalidPayload_ReturnsValidationProblem()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory, initialQuantity: 10.0000m);

        var response = await client.PostAsJsonAsync("/api/v1/inventory/inbound", new
        {
            warehouseId = Guid.Empty,
            materialId = seed.MaterialId,
            quantity = -1m,
            documentNo = "",
            note = new string('a', 513)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemApiModel>();
        Assert.NotNull(problem);
        Assert.NotNull(problem.Errors);
        Assert.Contains("warehouseId", problem.Errors.Keys);
        Assert.Contains("quantity", problem.Errors.Keys);
        Assert.Contains("documentNo", problem.Errors.Keys);
        Assert.Contains("note", problem.Errors.Keys);

        Assert.Equal(0, await CountMovementsAsync(factory, seed));
    }

    [Fact]
    public async Task PostTransactions_QuantityWithMoreThanFourDecimals_ReturnsValidationProblem()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory, initialQuantity: 10.0000m);

        var response = await client.PostAsJsonAsync(
            "/api/v1/inventory/inbound",
            BuildPayload(seed, 1.00001m, "PO-2026-0106"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemApiModel>();
        Assert.NotNull(problem?.Errors);
        Assert.Contains("quantity", problem.Errors.Keys);
    }

    [Fact]
    public async Task PostTransactions_UnknownWarehouseOrMaterial_ReturnsNotFound()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory, initialQuantity: 10.0000m);

        var unknownWarehouse = await client.PostAsJsonAsync("/api/v1/inventory/inbound", BuildPayload(
            seed with { WarehouseId = Guid.NewGuid() }, 1.0000m, "PO-2026-0107"));
        Assert.Equal(HttpStatusCode.NotFound, unknownWarehouse.StatusCode);
        Assert.Equal("application/problem+json", unknownWarehouse.Content.Headers.ContentType?.MediaType);

        var unknownMaterial = await client.PostAsJsonAsync("/api/v1/inventory/outbound", BuildPayload(
            seed with { MaterialId = Guid.NewGuid() }, 1.0000m, "SO-2026-0107", customerId: seed.CustomerId));
        Assert.Equal(HttpStatusCode.NotFound, unknownMaterial.StatusCode);
    }

    [Fact]
    public async Task PostTransactions_UnknownSupplierOrCustomer_ReturnsNotFound()
    {
        await using var factory = new InventoryApiFactory();
        var client = factory.CreateClient();
        var seed = await SeedAsync(factory, initialQuantity: 10.0000m);

        var unknownSupplier = await client.PostAsJsonAsync("/api/v1/inventory/inbound", BuildPayload(
            seed, 1.0000m, "PO-2026-0108", supplierId: Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, unknownSupplier.StatusCode);

        var unknownCustomer = await client.PostAsJsonAsync("/api/v1/inventory/outbound", BuildPayload(
            seed, 1.0000m, "SO-2026-0108", customerId: Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, unknownCustomer.StatusCode);

        Assert.Equal(0, await CountMovementsAsync(factory, seed));
    }

    private sealed record MovementApiModel(
        Guid Id,
        string MovementType,
        string DocumentNo,
        Guid WarehouseId,
        Guid MaterialId,
        decimal Quantity,
        Guid? SupplierId,
        Guid? CustomerId,
        DateTime OccurredAtUtc,
        string? Note,
        decimal OnHandQuantity);

    private sealed record BalanceApiModel(
        Guid Id,
        Guid WarehouseId,
        Guid MaterialId,
        decimal OnHandQuantity);

    private sealed record PagedApiResult(
        List<BalanceApiModel> Items,
        int Page,
        int PageSize,
        int TotalCount);

    private sealed record ValidationProblemApiModel(Dictionary<string, string[]>? Errors);
}
