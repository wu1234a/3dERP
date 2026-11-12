using ThreeDErp.Application.Common;
using ThreeDErp.Application.Inventory;
using ThreeDErp.Application.Materials;
using ThreeDErp.Application.Warehouses;
using ThreeDErp.Domain.Inventory;
using ThreeDErp.Domain.Materials;
using ThreeDErp.Domain.Warehouses;

namespace ThreeDErp.UnitTests.Inventory;

/// <summary>
/// InventoryAlertService 单元测试：校验 / 主数据存在性 / 仓库 × 物料唯一 / CRUD 流程 /
/// 分页规范化与预警映射。仓储用内存假实现。
/// </summary>
public sealed class InventoryAlertServiceTests
{
    private readonly FakeInventoryRepository _repository = new();
    private readonly FakeWarehouseRepository _warehouseRepository = new();
    private readonly FakeMaterialRepository _materialRepository = new();
    private readonly InventoryAlertService _service;

    public InventoryAlertServiceTests()
    {
        _service = new InventoryAlertService(
            _repository,
            _warehouseRepository,
            _materialRepository,
            new InventoryAlertRuleRequestValidator());
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

    private static InventoryAlertRule CreateRule(
        Warehouse warehouse,
        Material material,
        decimal minimumQuantity,
        bool isEnabled = true) => new()
    {
        Id = Guid.CreateVersion7(),
        WarehouseId = warehouse.Id,
        MaterialId = material.Id,
        MinimumQuantity = minimumQuantity,
        IsEnabled = isEnabled,
        Warehouse = warehouse,
        Material = material
    };

    private async Task<(Warehouse Warehouse, Material Material)> SeedMasterDataAsync(
        string warehouseCode = "WH-001",
        string materialCode = "MAT-RESIN-001")
    {
        var warehouse = CreateWarehouse(warehouseCode);
        var material = CreateMaterial(materialCode);
        _warehouseRepository.Warehouses.Add(warehouse);
        _materialRepository.Materials.Add(material);
        return (warehouse, material);
    }

    [Fact]
    public async Task CreateRuleAsync_ValidRequest_CreatesEnabledRule()
    {
        var (warehouse, material) = await SeedMasterDataAsync();

        var dto = await _service.CreateRuleAsync(
            new CreateInventoryAlertRuleRequest(warehouse.Id, material.Id, 25.5m));

        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Equal(warehouse.Id, dto.WarehouseId);
        Assert.Equal("WH-001", dto.WarehouseCode);
        Assert.Equal("主仓库", dto.WarehouseName);
        Assert.Equal(material.Id, dto.MaterialId);
        Assert.Equal("MAT-RESIN-001", dto.MaterialCode);
        Assert.Equal("光敏树脂", dto.MaterialName);
        Assert.Equal("kg", dto.MaterialUnit);
        Assert.Equal(25.5m, dto.MinimumQuantity);
        Assert.True(dto.IsEnabled);

        var stored = Assert.Single(_repository.AlertRules);
        Assert.Equal(dto.Id, stored.Id);
        Assert.Equal(warehouse.Id, stored.WarehouseId);
        Assert.Equal(material.Id, stored.MaterialId);
        Assert.Equal(25.5m, stored.MinimumQuantity);
        Assert.True(stored.IsEnabled);
    }

    [Fact]
    public async Task CreateRuleAsync_DisabledRequest_StoresDisabledRule()
    {
        var (warehouse, material) = await SeedMasterDataAsync();

        var dto = await _service.CreateRuleAsync(
            new CreateInventoryAlertRuleRequest(warehouse.Id, material.Id, 10m, IsEnabled: false));

        Assert.False(dto.IsEnabled);
        Assert.False(_repository.AlertRules[0].IsEnabled);
    }

    [Fact]
    public async Task CreateRuleAsync_InvalidRequest_ThrowsValidationExceptionWithoutSaving()
    {
        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateRuleAsync(
                new CreateInventoryAlertRuleRequest(Guid.NewGuid(), Guid.NewGuid(), -1m)));

        Assert.Contains("minimumQuantity", exception.Errors.Keys);
        Assert.Empty(_repository.AlertRules);
    }

    [Fact]
    public async Task CreateRuleAsync_UnknownWarehouse_ThrowsNotFoundException()
    {
        var material = CreateMaterial();
        _materialRepository.Materials.Add(material);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => _service.CreateRuleAsync(
                new CreateInventoryAlertRuleRequest(Guid.NewGuid(), material.Id, 10m)));

        Assert.Contains("仓库", exception.Message);
        Assert.Empty(_repository.AlertRules);
    }

    [Fact]
    public async Task CreateRuleAsync_UnknownMaterial_ThrowsNotFoundException()
    {
        var warehouse = CreateWarehouse();
        _warehouseRepository.Warehouses.Add(warehouse);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => _service.CreateRuleAsync(
                new CreateInventoryAlertRuleRequest(warehouse.Id, Guid.NewGuid(), 10m)));

        Assert.Contains("物料", exception.Message);
        Assert.Empty(_repository.AlertRules);
    }

    [Fact]
    public async Task CreateRuleAsync_DuplicatePair_ThrowsConflictException()
    {
        var (warehouse, material) = await SeedMasterDataAsync();
        await _service.CreateRuleAsync(
            new CreateInventoryAlertRuleRequest(warehouse.Id, material.Id, 10m));

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateRuleAsync(
                new CreateInventoryAlertRuleRequest(warehouse.Id, material.Id, 20m)));

        Assert.Contains("已存在", exception.Message);
        Assert.Single(_repository.AlertRules);
    }

    [Fact]
    public async Task CreateRuleAsync_SameMaterialInDifferentWarehouses_IsAllowed()
    {
        var (warehouse, material) = await SeedMasterDataAsync("WH-001");
        var secondWarehouse = CreateWarehouse("WH-002");
        _warehouseRepository.Warehouses.Add(secondWarehouse);

        await _service.CreateRuleAsync(
            new CreateInventoryAlertRuleRequest(warehouse.Id, material.Id, 10m));
        await _service.CreateRuleAsync(
            new CreateInventoryAlertRuleRequest(secondWarehouse.Id, material.Id, 5m));

        Assert.Equal(2, _repository.AlertRules.Count);
    }

    [Fact]
    public async Task UpdateRuleAsync_UpdatesFieldsAndKeepsId()
    {
        var (warehouse, material) = await SeedMasterDataAsync();
        var created = await _service.CreateRuleAsync(
            new CreateInventoryAlertRuleRequest(warehouse.Id, material.Id, 10m));

        var updated = await _service.UpdateRuleAsync(
            created.Id,
            new UpdateInventoryAlertRuleRequest(warehouse.Id, material.Id, 33.3333m, IsEnabled: false));

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(33.3333m, updated.MinimumQuantity);
        Assert.False(updated.IsEnabled);
        Assert.Equal(33.3333m, _repository.AlertRules[0].MinimumQuantity);
        Assert.False(_repository.AlertRules[0].IsEnabled);
    }

    [Fact]
    public async Task UpdateRuleAsync_ChangingToExistingPair_ThrowsConflictException()
    {
        var (warehouse, material) = await SeedMasterDataAsync();
        var secondWarehouse = CreateWarehouse("WH-002");
        _warehouseRepository.Warehouses.Add(secondWarehouse);

        var first = await _service.CreateRuleAsync(
            new CreateInventoryAlertRuleRequest(warehouse.Id, material.Id, 10m));
        await _service.CreateRuleAsync(
            new CreateInventoryAlertRuleRequest(secondWarehouse.Id, material.Id, 20m));

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _service.UpdateRuleAsync(
                first.Id,
                new UpdateInventoryAlertRuleRequest(secondWarehouse.Id, material.Id, 30m, IsEnabled: true)));

        Assert.Contains("已存在", exception.Message);
    }

    [Fact]
    public async Task UpdateRuleAsync_KeepingOwnPair_DoesNotConflict()
    {
        var (warehouse, material) = await SeedMasterDataAsync();
        var created = await _service.CreateRuleAsync(
            new CreateInventoryAlertRuleRequest(warehouse.Id, material.Id, 10m));

        var updated = await _service.UpdateRuleAsync(
            created.Id,
            new UpdateInventoryAlertRuleRequest(warehouse.Id, material.Id, 15m, IsEnabled: true));

        Assert.Equal(15m, updated.MinimumQuantity);
    }

    [Fact]
    public async Task UpdateRuleAsync_UnknownRule_ThrowsNotFoundException()
    {
        var (warehouse, material) = await SeedMasterDataAsync();

        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.UpdateRuleAsync(
                Guid.NewGuid(),
                new UpdateInventoryAlertRuleRequest(warehouse.Id, material.Id, 10m, IsEnabled: true)));
    }

    [Fact]
    public async Task UpdateRuleAsync_UnknownWarehouse_ThrowsNotFoundException()
    {
        var (warehouse, material) = await SeedMasterDataAsync();
        var created = await _service.CreateRuleAsync(
            new CreateInventoryAlertRuleRequest(warehouse.Id, material.Id, 10m));

        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.UpdateRuleAsync(
                created.Id,
                new UpdateInventoryAlertRuleRequest(Guid.NewGuid(), material.Id, 10m, IsEnabled: true)));
    }

    [Fact]
    public async Task UpdateRuleAsync_InvalidRequest_ThrowsValidationExceptionWithoutSaving()
    {
        var (warehouse, material) = await SeedMasterDataAsync();
        var created = await _service.CreateRuleAsync(
            new CreateInventoryAlertRuleRequest(warehouse.Id, material.Id, 10m));

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => _service.UpdateRuleAsync(
                created.Id,
                new UpdateInventoryAlertRuleRequest(warehouse.Id, material.Id, 10.00001m, IsEnabled: true)));

        Assert.Contains("minimumQuantity", exception.Errors.Keys);
        Assert.Equal(10m, _repository.AlertRules[0].MinimumQuantity);
    }

    [Fact]
    public async Task DeleteRuleAsync_RemovesRule()
    {
        var (warehouse, material) = await SeedMasterDataAsync();
        var created = await _service.CreateRuleAsync(
            new CreateInventoryAlertRuleRequest(warehouse.Id, material.Id, 10m));

        await _service.DeleteRuleAsync(created.Id);

        Assert.Empty(_repository.AlertRules);
    }

    [Fact]
    public async Task DeleteRuleAsync_UnknownId_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.DeleteRuleAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetRuleAsync_UnknownId_ReturnsNull()
    {
        Assert.Null(await _service.GetRuleAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetRuleAsync_ReturnsSummaryFromNavigations()
    {
        var (warehouse, material) = await SeedMasterDataAsync();
        _repository.AlertRules.Add(CreateRule(warehouse, material, 12m));

        var dto = await _service.GetRuleAsync(_repository.AlertRules[0].Id);

        Assert.NotNull(dto);
        Assert.Equal("WH-001", dto.WarehouseCode);
        Assert.Equal("MAT-RESIN-001", dto.MaterialCode);
        Assert.Equal(12m, dto.MinimumQuantity);
    }

    [Fact]
    public async Task SearchRulesAsync_PassesFiltersAndIncludeDisabled()
    {
        var (warehouse, material) = await SeedMasterDataAsync();
        _repository.AlertRules.Add(CreateRule(warehouse, material, 10m));
        _repository.AlertRules.Add(CreateRule(warehouse, material, 5m, isEnabled: false));

        await _service.SearchRulesAsync(2, 25, warehouse.Id, material.Id, includeDisabled: true);

        Assert.Equal(2, _repository.LastPage);
        Assert.Equal(25, _repository.LastPageSize);
        Assert.Equal(warehouse.Id, _repository.LastWarehouseId);
        Assert.Equal(material.Id, _repository.LastMaterialId);
        Assert.True(_repository.LastIncludeDisabled);

        var enabledOnly = await _service.SearchRulesAsync(1, 10, null, null, includeDisabled: false);
        var item = Assert.Single(enabledOnly.Items);
        Assert.True(item.IsEnabled);

        var all = await _service.SearchRulesAsync(1, 10, null, null, includeDisabled: true);
        Assert.Equal(2, all.TotalCount);
    }

    [Fact]
    public async Task SearchRulesAsync_ClampsPageAndPageSize()
    {
        await _service.SearchRulesAsync(0, 0, null, null, includeDisabled: false);

        Assert.Equal(1, _repository.LastPage);
        Assert.Equal(10, _repository.LastPageSize);

        await _service.SearchRulesAsync(-3, 500, null, null, includeDisabled: false);

        Assert.Equal(1, _repository.LastPage);
        Assert.Equal(100, _repository.LastPageSize);
    }

    [Fact]
    public async Task SearchAlertsAsync_ReturnsOnlyBalancesBelowMinimum()
    {
        var (warehouse, material) = await SeedMasterDataAsync();
        var secondMaterial = CreateMaterial("MAT-POWDER-001", "钛合金粉末");
        _materialRepository.Materials.Add(secondMaterial);

        _repository.AlertRules.Add(CreateRule(warehouse, material, 20m));
        _repository.AlertRules.Add(CreateRule(warehouse, secondMaterial, 5m));

        _repository.Balances.Add(new InventoryBalance
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            MaterialId = material.Id,
            OnHandQuantity = 8.25m,
            Warehouse = warehouse,
            Material = material
        });

        var result = await _service.SearchAlertsAsync(1, 10, null, null);

        // 余额 8.25 低于下限 20 触发预警；下限 5 无余额记录时现有量视为 0，同样触发。
        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.True(item.OnHandQuantity < item.MinimumQuantity));

        var alert = Assert.Single(result.Items, item => item.RuleId == _repository.AlertRules[0].Id);
        Assert.Equal(warehouse.Id, alert.WarehouseId);
        Assert.Equal("WH-001", alert.WarehouseCode);
        Assert.Equal("主仓库", alert.WarehouseName);
        Assert.Equal(material.Id, alert.MaterialId);
        Assert.Equal("MAT-RESIN-001", alert.MaterialCode);
        Assert.Equal("光敏树脂", alert.MaterialName);
        Assert.Equal("kg", alert.MaterialUnit);
        Assert.Equal(8.25m, alert.OnHandQuantity);
        Assert.Equal(20m, alert.MinimumQuantity);
        Assert.Equal(11.75m, alert.ShortageQuantity);
    }

    [Fact]
    public async Task SearchAlertsAsync_MissingBalanceRow_TreatedAsZeroOnHand()
    {
        var (warehouse, material) = await SeedMasterDataAsync();
        _repository.AlertRules.Add(CreateRule(warehouse, material, 10m));

        var result = await _service.SearchAlertsAsync(1, 10, null, null);

        var alert = Assert.Single(result.Items);
        Assert.Equal(0m, alert.OnHandQuantity);
        Assert.Equal(10m, alert.ShortageQuantity);
    }

    [Fact]
    public async Task SearchAlertsAsync_ExcludesDisabledRulesAndBalancesAtOrAboveMinimum()
    {
        var (warehouse, material) = await SeedMasterDataAsync();
        var secondMaterial = CreateMaterial("MAT-POWDER-001", "钛合金粉末");
        var thirdMaterial = CreateMaterial("MAT-FILAMENT-001", "PLA 线材");
        _materialRepository.Materials.AddRange(secondMaterial, thirdMaterial);

        _repository.AlertRules.Add(CreateRule(warehouse, material, 10m));
        _repository.AlertRules.Add(CreateRule(warehouse, secondMaterial, 10m, isEnabled: false));
        _repository.AlertRules.Add(CreateRule(warehouse, thirdMaterial, 10m));

        _repository.Balances.Add(new InventoryBalance
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            MaterialId = material.Id,
            OnHandQuantity = 10m,
            Warehouse = warehouse,
            Material = material
        });
        _repository.Balances.Add(new InventoryBalance
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            MaterialId = secondMaterial.Id,
            OnHandQuantity = 0m,
            Warehouse = warehouse,
            Material = secondMaterial
        });

        var result = await _service.SearchAlertsAsync(1, 10, null, null);

        // 等于下限不触发；停用规则不参与计算。
        var alert = Assert.Single(result.Items);
        Assert.Equal(thirdMaterial.Id, alert.MaterialId);
    }

    [Fact]
    public async Task SearchAlertsAsync_ClampsPagingAndPassesFilters()
    {
        var (warehouse, material) = await SeedMasterDataAsync();
        _repository.AlertRules.Add(CreateRule(warehouse, material, 10m));

        await _service.SearchAlertsAsync(0, 0, null, null);

        Assert.Equal(1, _repository.LastPage);
        Assert.Equal(10, _repository.LastPageSize);

        await _service.SearchAlertsAsync(-2, 500, warehouse.Id, material.Id);

        Assert.Equal(1, _repository.LastPage);
        Assert.Equal(100, _repository.LastPageSize);
        Assert.Equal(warehouse.Id, _repository.LastWarehouseId);
        Assert.Equal(material.Id, _repository.LastMaterialId);
    }

    [Fact]
    public async Task SearchAlertsAsync_EmptyRepository_ReturnsEmptyPagedResult()
    {
        var result = await _service.SearchAlertsAsync(1, 10, null, null);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }

    private sealed class FakeWarehouseRepository : IWarehouseRepository
    {
        public List<Warehouse> Warehouses { get; } = [];

        public Task<PagedResult<Warehouse>> FindAsync(
            int page,
            int pageSize,
            string? keyword,
            bool includeInactive,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("预警服务不使用仓库列表查询。");

        public Task<Warehouse?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Warehouses.FirstOrDefault(warehouse => warehouse.Id == id));

        public Task<bool> CodeExistsAsync(
            string code,
            Guid? excludeId = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("预警服务不使用编码查重。");

        public Task AddAsync(Warehouse warehouse, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("预警服务不创建仓库。");

        public Task UpdateAsync(Warehouse warehouse, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("预警服务不更新仓库。");
    }

    private sealed class FakeMaterialRepository : IMaterialRepository
    {
        public List<Material> Materials { get; } = [];

        public Task<PagedResult<Material>> FindAsync(
            int page,
            int pageSize,
            string? keyword,
            bool includeInactive,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("预警服务不使用物料列表查询。");

        public Task<Material?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Materials.FirstOrDefault(material => material.Id == id));

        public Task<bool> CodeExistsAsync(
            string code,
            Guid? excludeId = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("预警服务不使用编码查重。");

        public Task AddAsync(Material material, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("预警服务不创建物料。");

        public Task UpdateAsync(Material material, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("预警服务不更新物料。");
    }
}
