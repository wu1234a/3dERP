using ThreeDErp.Application.Common;
using ThreeDErp.Application.Inventory;
using ThreeDErp.Domain.Inventory;
using ThreeDErp.Domain.Materials;
using ThreeDErp.Domain.Warehouses;

namespace ThreeDErp.UnitTests.Inventory;

/// <summary>
/// InventoryService 单元测试：分页 / 关键字规范化、过滤参数透传、
/// 空结果与仓库 / 物料摘要映射。仓储用假实现。
/// </summary>
public sealed class InventoryServiceTests
{
    private readonly FakeInventoryRepository _repository = new();
    private readonly InventoryService _service;

    public InventoryServiceTests()
    {
        _service = new InventoryService(_repository);
    }

    private static Warehouse CreateWarehouse(string code = "WH-001", string name = "主仓库") => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = name,
        IsActive = true,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static Material CreateMaterial(string code = "MAT-RESIN-001", string name = "光敏树脂") => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = name,
        Unit = "kg",
        MaterialType = "树脂",
        IsActive = true,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static InventoryBalance CreateBalance(
        Warehouse warehouse,
        Material material,
        decimal onHandQuantity) => new()
    {
        Id = Guid.NewGuid(),
        WarehouseId = warehouse.Id,
        MaterialId = material.Id,
        OnHandQuantity = onHandQuantity,
        Warehouse = warehouse,
        Material = material
    };

    [Fact]
    public async Task SearchBalancesAsync_EmptyRepository_ReturnsEmptyPagedResult()
    {
        var result = await _service.SearchBalancesAsync(1, 10, null, null, null);

        Assert.Empty(result.Items);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
        Assert.False(result.HasPreviousPage);
        Assert.False(result.HasNextPage);
    }

    [Fact]
    public async Task SearchBalancesAsync_PassesFiltersToRepositoryWithTrimmedKeyword()
    {
        var warehouseId = Guid.NewGuid();
        var materialId = Guid.NewGuid();

        await _service.SearchBalancesAsync(2, 25, warehouseId, materialId, " POWDER ");

        Assert.Equal(2, _repository.LastPage);
        Assert.Equal(25, _repository.LastPageSize);
        Assert.Equal(warehouseId, _repository.LastWarehouseId);
        Assert.Equal(materialId, _repository.LastMaterialId);
        Assert.Equal("POWDER", _repository.LastKeyword);
    }

    [Fact]
    public async Task SearchBalancesAsync_BlankKeyword_IsPassedAsNull()
    {
        await _service.SearchBalancesAsync(1, 10, null, null, "   ");

        Assert.Null(_repository.LastKeyword);
    }

    [Fact]
    public async Task SearchBalancesAsync_ClampsPageAndPageSize()
    {
        await _service.SearchBalancesAsync(0, 0, null, null, null);
        Assert.Equal(1, _repository.LastPage);
        Assert.Equal(10, _repository.LastPageSize);

        await _service.SearchBalancesAsync(-5, 500, null, null, null);
        Assert.Equal(1, _repository.LastPage);
        Assert.Equal(100, _repository.LastPageSize);
    }

    [Fact]
    public async Task SearchBalancesAsync_MapsWarehouseAndMaterialSummaries()
    {
        var warehouse = CreateWarehouse();
        var material = CreateMaterial();
        _repository.Balances.Add(CreateBalance(warehouse, material, 120.5000m));

        var result = await _service.SearchBalancesAsync(1, 10, null, null, null);

        var item = Assert.Single(result.Items);
        Assert.Equal(warehouse.Id, item.WarehouseId);
        Assert.Equal("WH-001", item.WarehouseCode);
        Assert.Equal("主仓库", item.WarehouseName);
        Assert.Equal(material.Id, item.MaterialId);
        Assert.Equal("MAT-RESIN-001", item.MaterialCode);
        Assert.Equal("光敏树脂", item.MaterialName);
        Assert.Equal("kg", item.MaterialUnit);
        Assert.Equal(120.5000m, item.OnHandQuantity);
    }

    [Fact]
    public async Task SearchBalancesAsync_FiltersByWarehouseMaterialAndKeyword()
    {
        var mainWarehouse = CreateWarehouse("WH-001");
        var secondWarehouse = CreateWarehouse("WH-002");
        var resin = CreateMaterial("MAT-RESIN-001", "光敏树脂");
        var powder = CreateMaterial("MAT-POWDER-001", "钛合金粉末");

        _repository.Balances.AddRange(
            CreateBalance(mainWarehouse, resin, 120.5000m),
            CreateBalance(mainWarehouse, powder, 8.2500m),
            CreateBalance(secondWarehouse, resin, 45.0000m));

        var byWarehouse = await _service.SearchBalancesAsync(1, 10, mainWarehouse.Id, null, null);
        Assert.Equal(2, byWarehouse.TotalCount);
        Assert.All(byWarehouse.Items, item => Assert.Equal(mainWarehouse.Id, item.WarehouseId));

        var byWarehouseAndMaterial = await _service.SearchBalancesAsync(
            1, 10, secondWarehouse.Id, resin.Id, null);
        var exactItem = Assert.Single(byWarehouseAndMaterial.Items);
        Assert.Equal(45.0000m, exactItem.OnHandQuantity);

        var byCodeKeyword = await _service.SearchBalancesAsync(1, 10, null, null, "POWDER");
        var codeItem = Assert.Single(byCodeKeyword.Items);
        Assert.Equal("MAT-POWDER-001", codeItem.MaterialCode);

        var byNameKeyword = await _service.SearchBalancesAsync(1, 10, null, null, "树脂");
        Assert.Equal(2, byNameKeyword.TotalCount);

        var unknownWarehouse = await _service.SearchBalancesAsync(1, 10, Guid.NewGuid(), null, null);
        Assert.Empty(unknownWarehouse.Items);
        Assert.Equal(0, unknownWarehouse.TotalCount);
    }

    [Fact]
    public async Task SearchBalancesAsync_PreservesRepositoryPagingMetadata()
    {
        var warehouse = CreateWarehouse();
        for (var index = 0; index < 5; index++)
        {
            var material = CreateMaterial($"MAT-{index:D3}", $"物料 {index}");
            _repository.Balances.Add(CreateBalance(warehouse, material, index));
        }

        var result = await _service.SearchBalancesAsync(2, 2, null, null, null);

        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.True(result.HasPreviousPage);
        Assert.True(result.HasNextPage);
    }

    /// <summary>
    /// 查询专用假仓储；写入端口（出入库事务）不属于 InventoryService 的职责范围，
    /// 由 InventoryTransactionServiceTests 中的专用假仓储覆盖。
    /// </summary>
    private sealed class FakeInventoryRepository : IInventoryRepository
    {
        public List<InventoryBalance> Balances { get; } = [];

        public int? LastPage { get; private set; }

        public int? LastPageSize { get; private set; }

        public Guid? LastWarehouseId { get; private set; }

        public Guid? LastMaterialId { get; private set; }

        public string? LastKeyword { get; private set; }

        public Task<PagedResult<InventoryBalance>> FindBalancesAsync(
            int page,
            int pageSize,
            Guid? warehouseId,
            Guid? materialId,
            string? keyword,
            CancellationToken cancellationToken = default)
        {
            LastPage = page;
            LastPageSize = pageSize;
            LastWarehouseId = warehouseId;
            LastMaterialId = materialId;
            LastKeyword = keyword;

            IEnumerable<InventoryBalance> query = Balances;

            if (warehouseId is not null)
            {
                query = query.Where(balance => balance.WarehouseId == warehouseId);
            }

            if (materialId is not null)
            {
                query = query.Where(balance => balance.MaterialId == materialId);
            }

            if (keyword is not null)
            {
                query = query.Where(balance =>
                    balance.Material.Code.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || balance.Material.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            }

            var ordered = query
                .OrderBy(balance => balance.Warehouse.Code)
                .ThenBy(balance => balance.Material.Code)
                .ToList();
            var items = ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Task.FromResult(new PagedResult<InventoryBalance>(items, page, pageSize, ordered.Count));
        }

        public Task<InventoryBalance?> FindBalanceAsync(
            Guid warehouseId,
            Guid materialId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<InventoryBalance?>(null);

        public Task<bool> DocumentNoExistsAsync(
            InventoryMovementType movementType,
            string documentNo,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> WarehouseExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> MaterialExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> SupplierExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> CustomerExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<InventoryBalance> RecordMovementAsync(
            InventoryMovement movement,
            InventoryBalance? balance,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("查询测试不涉及库存事务写入。");
    }
}
