using ThreeDErp.Application.Common;
using ThreeDErp.Application.Inventory;
using ThreeDErp.Domain.Inventory;
using ThreeDErp.Domain.Materials;
using ThreeDErp.Domain.Warehouses;

namespace ThreeDErp.UnitTests.Inventory;

/// <summary>
/// InventoryAlertService 单元测试：规则新增 / 修改 / 启停、重复规则与无效标识处理、
/// 校验失败、分页规范化，以及实时预警到 DTO 的映射。仓储用假实现
/// （模拟"Include 主数据导航 + 启用规则 × 余额实时联接"）。
/// </summary>
public sealed class InventoryAlertServiceTests
{
    private readonly FakeInventoryAlertRepository _repository = new();
    private readonly InventoryAlertService _service;

    public InventoryAlertServiceTests()
    {
        _service = new InventoryAlertService(_repository, new InventoryAlertRuleRequestValidator());
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

    private static CreateInventoryAlertRuleRequest CreateRequest(
        Guid? warehouseId = null,
        Guid? materialId = null,
        decimal minimumQuantity = 10.0000m,
        bool isEnabled = true) =>
        new(warehouseId ?? Guid.NewGuid(), materialId ?? Guid.NewGuid(), minimumQuantity, isEnabled);

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
    public async Task CreateRuleAsync_ValidRequest_CreatesEnabledRuleWithSummaries()
    {
        var warehouse = _repository.AddWarehouse(CreateWarehouse());
        var material = _repository.AddMaterial(CreateMaterial());

        var dto = await _service.CreateRuleAsync(new CreateInventoryAlertRuleRequest(
            warehouse.Id, material.Id, 12.5000m));

        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Equal(warehouse.Id, dto.WarehouseId);
        Assert.Equal("WH-001", dto.WarehouseCode);
        Assert.Equal("主仓库", dto.WarehouseName);
        Assert.Equal(material.Id, dto.MaterialId);
        Assert.Equal("MAT-RESIN-001", dto.MaterialCode);
        Assert.Equal("光敏树脂", dto.MaterialName);
        Assert.Equal("kg", dto.MaterialUnit);
        Assert.Equal(12.5000m, dto.MinimumQuantity);
        Assert.True(dto.IsEnabled);

        var stored = Assert.Single(_repository.Rules);
        Assert.Equal(dto.Id, stored.Id);
        Assert.Equal(12.5000m, stored.MinimumQuantity);
        Assert.True(stored.IsEnabled);
    }

    [Fact]
    public async Task CreateRuleAsync_OmittedIsEnabled_DefaultsToEnabled()
    {
        var warehouse = _repository.AddWarehouse(CreateWarehouse());
        var material = _repository.AddMaterial(CreateMaterial());

        var dto = await _service.CreateRuleAsync(
            new CreateInventoryAlertRuleRequest(warehouse.Id, material.Id, 5m));

        Assert.True(dto.IsEnabled);
    }

    [Fact]
    public async Task CreateRuleAsync_InvalidRequest_ThrowsValidationExceptionWithoutSaving()
    {
        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateRuleAsync(new CreateInventoryAlertRuleRequest(Guid.Empty, Guid.Empty, -1m)));

        Assert.Contains("warehouseId", exception.Errors.Keys);
        Assert.Contains("materialId", exception.Errors.Keys);
        Assert.Contains("minimumQuantity", exception.Errors.Keys);
        Assert.Empty(_repository.Rules);
    }

    [Fact]
    public async Task CreateRuleAsync_DuplicateScope_ThrowsConflictException()
    {
        var warehouse = _repository.AddWarehouse(CreateWarehouse());
        var material = _repository.AddMaterial(CreateMaterial());
        await _service.CreateRuleAsync(new CreateInventoryAlertRuleRequest(warehouse.Id, material.Id, 10m));

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateRuleAsync(new CreateInventoryAlertRuleRequest(warehouse.Id, material.Id, 20m)));

        Assert.Contains("已存在预警规则", exception.Message);
        Assert.Single(_repository.Rules);
    }

    [Fact]
    public async Task CreateRuleAsync_UnknownWarehouse_ThrowsNotFoundException()
    {
        var material = _repository.AddMaterial(CreateMaterial());

        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.CreateRuleAsync(new CreateInventoryAlertRuleRequest(Guid.NewGuid(), material.Id, 10m)));

        Assert.Empty(_repository.Rules);
    }

    [Fact]
    public async Task CreateRuleAsync_UnknownMaterial_ThrowsNotFoundException()
    {
        var warehouse = _repository.AddWarehouse(CreateWarehouse());

        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.CreateRuleAsync(new CreateInventoryAlertRuleRequest(warehouse.Id, Guid.NewGuid(), 10m)));

        Assert.Empty(_repository.Rules);
    }

    [Fact]
    public async Task UpdateRuleAsync_EditsThresholdAndScope()
    {
        var mainWarehouse = _repository.AddWarehouse(CreateWarehouse("WH-001", "主仓库"));
        var secondWarehouse = _repository.AddWarehouse(CreateWarehouse("WH-002", "分仓库"));
        var material = _repository.AddMaterial(CreateMaterial());
        var created = await _service.CreateRuleAsync(
            new CreateInventoryAlertRuleRequest(mainWarehouse.Id, material.Id, 10m));

        var updated = await _service.UpdateRuleAsync(created.Id, new UpdateInventoryAlertRuleRequest(
            secondWarehouse.Id, material.Id, 25.5000m, IsEnabled: true));

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(secondWarehouse.Id, updated.WarehouseId);
        Assert.Equal("WH-002", updated.WarehouseCode);
        Assert.Equal("分仓库", updated.WarehouseName);
        Assert.Equal(25.5000m, updated.MinimumQuantity);
        Assert.True(updated.IsEnabled);
    }

    [Fact]
    public async Task UpdateRuleAsync_DisablesRule()
    {
        var warehouse = _repository.AddWarehouse(CreateWarehouse());
        var material = _repository.AddMaterial(CreateMaterial());
        var created = await _service.CreateRuleAsync(
            new CreateInventoryAlertRuleRequest(warehouse.Id, material.Id, 10m));

        var updated = await _service.UpdateRuleAsync(
            created.Id,
            new UpdateInventoryAlertRuleRequest(warehouse.Id, material.Id, 10m, IsEnabled: false));

        Assert.False(updated.IsEnabled);
        Assert.False(_repository.Rules[0].IsEnabled);
    }

    [Fact]
    public async Task UpdateRuleAsync_UnknownId_ThrowsNotFoundException()
    {
        var warehouse = _repository.AddWarehouse(CreateWarehouse());
        var material = _repository.AddMaterial(CreateMaterial());

        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.UpdateRuleAsync(
                Guid.NewGuid(),
                new UpdateInventoryAlertRuleRequest(warehouse.Id, material.Id, 10m, IsEnabled: true)));
    }

    [Fact]
    public async Task UpdateRuleAsync_ScopeCollisionWithOtherRule_ThrowsConflictException()
    {
        var warehouse = _repository.AddWarehouse(CreateWarehouse());
        var resin = _repository.AddMaterial(CreateMaterial("MAT-RESIN-001", "光敏树脂"));
        var powder = _repository.AddMaterial(CreateMaterial("MAT-POWDER-001", "钛合金粉末"));
        var first = await _service.CreateRuleAsync(
            new CreateInventoryAlertRuleRequest(warehouse.Id, resin.Id, 10m));
        await _service.CreateRuleAsync(new CreateInventoryAlertRuleRequest(warehouse.Id, powder.Id, 20m));

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _service.UpdateRuleAsync(first.Id, new UpdateInventoryAlertRuleRequest(
                warehouse.Id, powder.Id, 30m, IsEnabled: true)));

        Assert.Contains("已存在预警规则", exception.Message);
    }

    [Fact]
    public async Task UpdateRuleAsync_KeepingOwnScope_DoesNotConflict()
    {
        var warehouse = _repository.AddWarehouse(CreateWarehouse());
        var material = _repository.AddMaterial(CreateMaterial());
        var created = await _service.CreateRuleAsync(
            new CreateInventoryAlertRuleRequest(warehouse.Id, material.Id, 10m));

        var updated = await _service.UpdateRuleAsync(created.Id, new UpdateInventoryAlertRuleRequest(
            warehouse.Id, material.Id, 42m, IsEnabled: true));

        Assert.Equal(42m, updated.MinimumQuantity);
    }

    [Fact]
    public async Task UpdateRuleAsync_InvalidRequest_ThrowsValidationExceptionWithoutSaving()
    {
        var warehouse = _repository.AddWarehouse(CreateWarehouse());
        var material = _repository.AddMaterial(CreateMaterial());
        var created = await _service.CreateRuleAsync(
            new CreateInventoryAlertRuleRequest(warehouse.Id, material.Id, 10m));

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => _service.UpdateRuleAsync(created.Id, new UpdateInventoryAlertRuleRequest(
                warehouse.Id, material.Id, -5m, IsEnabled: true)));

        Assert.Contains("minimumQuantity", exception.Errors.Keys);
        Assert.Equal(10m, _repository.Rules[0].MinimumQuantity);
    }

    [Fact]
    public async Task SearchRulesAsync_PassesFiltersWithClampedPaging()
    {
        var warehouseId = Guid.NewGuid();
        var materialId = Guid.NewGuid();

        await _service.SearchRulesAsync(0, 500, warehouseId, materialId, isEnabled: true, keyword: " 树脂 ");
        Assert.Equal(1, _repository.LastRuleQuery!.Value.Page);
        Assert.Equal(100, _repository.LastRuleQuery!.Value.PageSize);
        Assert.Equal(warehouseId, _repository.LastRuleQuery!.Value.WarehouseId);
        Assert.Equal(materialId, _repository.LastRuleQuery!.Value.MaterialId);
        Assert.True(_repository.LastRuleQuery!.Value.IsEnabled);
        Assert.Equal("树脂", _repository.LastRuleQuery!.Value.Keyword);

        await _service.SearchRulesAsync(3, 0, null, null, null, null);
        Assert.Equal(3, _repository.LastRuleQuery!.Value.Page);
        Assert.Equal(10, _repository.LastRuleQuery!.Value.PageSize);
        Assert.Null(_repository.LastRuleQuery!.Value.Keyword);
    }

    [Fact]
    public async Task SearchAlertsAsync_MapsRowsWithInventoryDimensionAndRule()
    {
        var warehouse = _repository.AddWarehouse(CreateWarehouse("WH-A", "第一仓库"));
        var material = _repository.AddMaterial(CreateMaterial("MAT-POWDER-001", "钛合金粉末"));
        var rule = await _service.CreateRuleAsync(
            new CreateInventoryAlertRuleRequest(warehouse.Id, material.Id, 10.0000m));
        _repository.Balances.Add(CreateBalance(warehouse, material, 8.2500m));

        var result = await _service.SearchAlertsAsync(1, 10, null, null, null);

        var alert = Assert.Single(result.Items);
        Assert.Equal(warehouse.Id, alert.WarehouseId);
        Assert.Equal("WH-A", alert.WarehouseCode);
        Assert.Equal("第一仓库", alert.WarehouseName);
        Assert.Equal(material.Id, alert.MaterialId);
        Assert.Equal("MAT-POWDER-001", alert.MaterialCode);
        Assert.Equal("钛合金粉末", alert.MaterialName);
        Assert.Equal("kg", alert.MaterialUnit);
        Assert.Equal(8.2500m, alert.OnHandQuantity);
        Assert.Equal(rule.Id, alert.RuleId);
        Assert.Equal(10.0000m, alert.MinimumQuantity);
        Assert.Equal(InventoryAlertTypes.LowStock, alert.AlertType);
    }

    [Fact]
    public async Task SearchAlertsAsync_ReflectsRuleChangesOnNextQuery()
    {
        var warehouse = _repository.AddWarehouse(CreateWarehouse());
        var material = _repository.AddMaterial(CreateMaterial());
        var rule = await _service.CreateRuleAsync(
            new CreateInventoryAlertRuleRequest(warehouse.Id, material.Id, 10m));
        _repository.Balances.Add(CreateBalance(warehouse, material, 8m));

        Assert.Single((await _service.SearchAlertsAsync(1, 10, null, null, null)).Items);

        // 提高阈值后仍在预警
        await _service.UpdateRuleAsync(rule.Id, new UpdateInventoryAlertRuleRequest(
            warehouse.Id, material.Id, 100m, IsEnabled: true));
        var raised = await _service.SearchAlertsAsync(1, 10, null, null, null);
        var raisedAlert = Assert.Single(raised.Items);
        Assert.Equal(100m, raisedAlert.MinimumQuantity);

        // 降低阈值到余额之下，不再预警
        await _service.UpdateRuleAsync(rule.Id, new UpdateInventoryAlertRuleRequest(
            warehouse.Id, material.Id, 5m, IsEnabled: true));
        Assert.Empty((await _service.SearchAlertsAsync(1, 10, null, null, null)).Items);

        // 停用规则后不再预警，重新启用后恢复
        await _service.UpdateRuleAsync(rule.Id, new UpdateInventoryAlertRuleRequest(
            warehouse.Id, material.Id, 100m, IsEnabled: false));
        Assert.Empty((await _service.SearchAlertsAsync(1, 10, null, null, null)).Items);

        await _service.UpdateRuleAsync(rule.Id, new UpdateInventoryAlertRuleRequest(
            warehouse.Id, material.Id, 100m, IsEnabled: true));
        Assert.Single((await _service.SearchAlertsAsync(1, 10, null, null, null)).Items);
    }

    [Fact]
    public async Task SearchAlertsAsync_EmptyRepository_ReturnsEmptyPagedResult()
    {
        var result = await _service.SearchAlertsAsync(1, 10, null, null, null);

        Assert.Empty(result.Items);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public async Task SearchAlertsAsync_ClampsPagingAndPassesFilters()
    {
        var warehouseId = Guid.NewGuid();
        var materialId = Guid.NewGuid();

        await _service.SearchAlertsAsync(-1, 0, warehouseId, materialId, " POWDER ");
        Assert.Equal(1, _repository.LastAlertQuery!.Value.Page);
        Assert.Equal(10, _repository.LastAlertQuery!.Value.PageSize);
        Assert.Equal(warehouseId, _repository.LastAlertQuery!.Value.WarehouseId);
        Assert.Equal(materialId, _repository.LastAlertQuery!.Value.MaterialId);
        Assert.Equal("POWDER", _repository.LastAlertQuery!.Value.Keyword);

        await _service.SearchAlertsAsync(2, 1000, null, null, "  ");
        Assert.Equal(2, _repository.LastAlertQuery!.Value.Page);
        Assert.Equal(100, _repository.LastAlertQuery!.Value.PageSize);
        Assert.Null(_repository.LastAlertQuery!.Value.Keyword);
    }

    [Fact]
    public async Task SearchRulesAsync_MapsMultipleRulesAsSummaries()
    {
        var warehouse = _repository.AddWarehouse(CreateWarehouse("WH-A", "第一仓库"));
        var resin = _repository.AddMaterial(CreateMaterial("MAT-RESIN-001", "光敏树脂"));
        var powder = _repository.AddMaterial(CreateMaterial("MAT-POWDER-001", "钛合金粉末"));
        await _service.CreateRuleAsync(new CreateInventoryAlertRuleRequest(warehouse.Id, resin.Id, 10m));
        await _service.CreateRuleAsync(new CreateInventoryAlertRuleRequest(warehouse.Id, powder.Id, 20m, IsEnabled: false));

        var result = await _service.SearchRulesAsync(1, 10, null, null, null, null);

        Assert.Equal(2, result.TotalCount);
        Assert.Collection(result.Items,
            item =>
            {
                Assert.Equal("MAT-POWDER-001", item.MaterialCode);
                Assert.Equal(20m, item.MinimumQuantity);
                Assert.False(item.IsEnabled);
            },
            item => Assert.Equal("MAT-RESIN-001", item.MaterialCode));
    }

    private sealed class FakeInventoryAlertRepository : IInventoryAlertRepository
    {
        private readonly Dictionary<Guid, Warehouse> _warehouses = [];
        private readonly Dictionary<Guid, Material> _materials = [];

        public List<InventoryAlertRule> Rules { get; } = [];

        public List<InventoryBalance> Balances { get; } = [];

        public (int Page, int PageSize, Guid? WarehouseId, Guid? MaterialId, bool? IsEnabled, string? Keyword)? LastRuleQuery { get; private set; }

        public (int Page, int PageSize, Guid? WarehouseId, Guid? MaterialId, string? Keyword)? LastAlertQuery { get; private set; }

        public Warehouse AddWarehouse(Warehouse warehouse)
        {
            _warehouses[warehouse.Id] = warehouse;
            return warehouse;
        }

        public Material AddMaterial(Material material)
        {
            _materials[material.Id] = material;
            return material;
        }

        public Task<PagedResult<InventoryAlertRule>> FindRulesAsync(
            int page,
            int pageSize,
            Guid? warehouseId,
            Guid? materialId,
            bool? isEnabled,
            string? keyword,
            CancellationToken cancellationToken = default)
        {
            LastRuleQuery = (page, pageSize, warehouseId, materialId, isEnabled, keyword);

            IEnumerable<InventoryAlertRule> query = Rules;

            if (warehouseId is not null)
            {
                query = query.Where(rule => rule.WarehouseId == warehouseId);
            }

            if (materialId is not null)
            {
                query = query.Where(rule => rule.MaterialId == materialId);
            }

            if (isEnabled is not null)
            {
                query = query.Where(rule => rule.IsEnabled == isEnabled);
            }

            if (keyword is not null)
            {
                query = query.Where(rule =>
                    rule.Material.Code.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || rule.Material.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            }

            var ordered = query
                .OrderBy(rule => _warehouses[rule.WarehouseId].Code)
                .ThenBy(rule => _materials[rule.MaterialId].Code)
                .ToList();
            var items = ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Task.FromResult(new PagedResult<InventoryAlertRule>(items, page, pageSize, ordered.Count));
        }

        /// <summary>模拟 EF 的 Include：返回的规则带主数据导航属性。</summary>
        public Task<InventoryAlertRule?> FindRuleByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var rule = Rules.FirstOrDefault(candidate => candidate.Id == id);
            if (rule is not null)
            {
                rule.Warehouse = _warehouses[rule.WarehouseId];
                rule.Material = _materials[rule.MaterialId];
            }

            return Task.FromResult(rule);
        }

        public Task<bool> RuleExistsAsync(
            Guid warehouseId,
            Guid materialId,
            Guid? excludeRuleId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Rules.Any(rule =>
                rule.WarehouseId == warehouseId
                && rule.MaterialId == materialId
                && (excludeRuleId is null || rule.Id != excludeRuleId)));

        public Task<bool> WarehouseExistsAsync(Guid warehouseId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_warehouses.ContainsKey(warehouseId));

        public Task<bool> MaterialExistsAsync(Guid materialId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_materials.ContainsKey(materialId));

        public Task AddRuleAsync(InventoryAlertRule rule, CancellationToken cancellationToken = default)
        {
            Rules.Add(rule);
            return Task.CompletedTask;
        }

        public Task UpdateRuleAsync(InventoryAlertRule rule, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        /// <summary>模拟实时预警联接：启用规则 × 当前余额，余额低于下限命中。</summary>
        public Task<PagedResult<InventoryAlertRow>> FindAlertsAsync(
            int page,
            int pageSize,
            Guid? warehouseId,
            Guid? materialId,
            string? keyword,
            CancellationToken cancellationToken = default)
        {
            LastAlertQuery = (page, pageSize, warehouseId, materialId, keyword);

            var rows = from rule in Rules
                       where rule.IsEnabled
                       join balance in Balances
                           on new { rule.WarehouseId, rule.MaterialId }
                           equals new { balance.WarehouseId, balance.MaterialId }
                       where balance.OnHandQuantity < rule.MinimumQuantity
                       let warehouse = _warehouses[rule.WarehouseId]
                       let material = _materials[rule.MaterialId]
                       select new InventoryAlertRow(
                           balance.WarehouseId,
                           warehouse.Code,
                           warehouse.Name,
                           balance.MaterialId,
                           material.Code,
                           material.Name,
                           material.Unit,
                           balance.OnHandQuantity,
                           rule.Id,
                           rule.MinimumQuantity);

            if (warehouseId is not null)
            {
                rows = rows.Where(row => row.WarehouseId == warehouseId);
            }

            if (materialId is not null)
            {
                rows = rows.Where(row => row.MaterialId == materialId);
            }

            if (keyword is not null)
            {
                rows = rows.Where(row =>
                    row.MaterialCode.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || row.MaterialName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            }

            var ordered = rows
                .OrderBy(row => row.WarehouseCode)
                .ThenBy(row => row.MaterialCode)
                .ToList();
            var items = ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Task.FromResult(new PagedResult<InventoryAlertRow>(items, page, pageSize, ordered.Count));
        }
    }
}
