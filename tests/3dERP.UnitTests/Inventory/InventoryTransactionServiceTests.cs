using ThreeDErp.Application.Common;
using ThreeDErp.Application.Inventory;
using ThreeDErp.Domain.Inventory;

namespace ThreeDErp.UnitTests.Inventory;

/// <summary>
/// InventoryTransactionService 单元测试：入库 / 出库余额方向、首入库建余额、
/// 禁止负库存、单据号防重（同类型）/ 跨类型放行、主数据存在性与请求校验拦截。
/// 仓储用内存假实现。
/// </summary>
public sealed class InventoryTransactionServiceTests
{
    private readonly FakeInventoryRepository _repository = new();
    private readonly InventoryTransactionService _service;

    public InventoryTransactionServiceTests()
    {
        _service = new InventoryTransactionService(_repository, new InventoryRequestValidator());
    }

    private static InventoryTransactionRequest CreateRequest(
        InventoryTransactionRequest? seed = null) => seed ?? new InventoryTransactionRequest(
        Guid.NewGuid(),
        Guid.NewGuid(),
        25.0000m,
        "PO-2026-0001",
        Guid.NewGuid(),
        null,
        "采购收货");

    private static InventoryBalance SeedBalance(Guid warehouseId, Guid materialId, decimal quantity) => new()
    {
        Id = Guid.NewGuid(),
        WarehouseId = warehouseId,
        MaterialId = materialId,
        OnHandQuantity = quantity
    };

    [Fact]
    public async Task RecordInboundAsync_ExistingBalance_IncrementsAndWritesMovement()
    {
        var request = CreateRequest();
        _repository.RegisterMasterData(request);
        _repository.Balances.Add(SeedBalance(request.WarehouseId, request.MaterialId, 100.0000m));

        var result = await _service.RecordInboundAsync(request);

        Assert.Equal(InventoryMovementType.In.ToString(), result.MovementType);
        Assert.Equal("PO-2026-0001", result.DocumentNo);
        Assert.Equal(25.0000m, result.Quantity);
        Assert.Equal(request.SupplierId, result.SupplierId);
        Assert.Equal("采购收货", result.Note);
        Assert.Equal(125.0000m, result.OnHandQuantity);
        Assert.Equal(125.0000m, _repository.Balances.Single().OnHandQuantity);

        var movement = Assert.Single(_repository.Movements);
        Assert.Equal(InventoryMovementType.In, movement.MovementType);
        Assert.Equal(request.WarehouseId, movement.WarehouseId);
        Assert.Equal(request.MaterialId, movement.MaterialId);
        Assert.Equal(25.0000m, movement.Quantity);
        Assert.Equal(request.SupplierId, movement.SupplierId);
        Assert.Null(movement.CustomerId);
        Assert.Equal("采购收货", movement.Remark);
    }

    [Fact]
    public async Task RecordInboundAsync_NoBalance_CreatesBalanceWithMovementQuantity()
    {
        var request = CreateRequest();
        _repository.RegisterMasterData(request);

        var result = await _service.RecordInboundAsync(request);

        var balance = Assert.Single(_repository.Balances);
        Assert.Equal(request.WarehouseId, balance.WarehouseId);
        Assert.Equal(request.MaterialId, balance.MaterialId);
        Assert.Equal(25.0000m, balance.OnHandQuantity);
        Assert.Equal(25.0000m, result.OnHandQuantity);
    }

    [Fact]
    public async Task RecordOutboundAsync_EnoughStock_DecrementsAndWritesMovement()
    {
        var request = CreateRequest() with
        {
            DocumentNo = "SO-2026-0001",
            SupplierId = null,
            CustomerId = Guid.NewGuid(),
            Note = "销售发货"
        };
        _repository.RegisterMasterData(request);
        _repository.Balances.Add(SeedBalance(request.WarehouseId, request.MaterialId, 80.0000m));

        var result = await _service.RecordOutboundAsync(request);

        Assert.Equal(InventoryMovementType.Out.ToString(), result.MovementType);
        Assert.Equal(55.0000m, result.OnHandQuantity);
        Assert.Equal(request.CustomerId, result.CustomerId);
        Assert.Null(result.SupplierId);

        var movement = Assert.Single(_repository.Movements);
        Assert.Equal(InventoryMovementType.Out, movement.MovementType);
        Assert.Equal(request.CustomerId, movement.CustomerId);
        Assert.Null(movement.SupplierId);
    }

    [Fact]
    public async Task RecordOutboundAsync_NoBalance_ThrowsConflict()
    {
        var request = CreateRequest() with { DocumentNo = "SO-2026-0001", SupplierId = null };
        _repository.RegisterMasterData(request);

        var exception = await Assert.ThrowsAsync<ThreeDErp.Application.Common.ConflictException>(
            () => _service.RecordOutboundAsync(request));

        Assert.Contains("库存不足", exception.Message);
        Assert.Empty(_repository.Movements);
        Assert.Equal(0, _repository.RecordMovementCallCount);
    }

    [Fact]
    public async Task RecordOutboundAsync_InsufficientStock_ThrowsConflictWithoutWriting()
    {
        var request = CreateRequest() with { Quantity = 100.5000m, DocumentNo = "SO-2026-0001", SupplierId = null };
        _repository.RegisterMasterData(request);
        _repository.Balances.Add(SeedBalance(request.WarehouseId, request.MaterialId, 100.0000m));

        var exception = await Assert.ThrowsAsync<ThreeDErp.Application.Common.ConflictException>(
            () => _service.RecordOutboundAsync(request));

        Assert.Contains("100", exception.Message);
        Assert.Empty(_repository.Movements);
        Assert.Equal(0, _repository.RecordMovementCallCount);
        Assert.Equal(100.0000m, _repository.Balances.Single().OnHandQuantity);
    }

    [Fact]
    public async Task RecordOutboundAsync_ExactAvailableQuantity_IsAllowedDownToZero()
    {
        var request = CreateRequest() with { Quantity = 25.0000m, DocumentNo = "SO-2026-0001", SupplierId = null };
        _repository.RegisterMasterData(request);
        _repository.Balances.Add(SeedBalance(request.WarehouseId, request.MaterialId, 25.0000m));

        var result = await _service.RecordOutboundAsync(request);

        Assert.Equal(0.0000m, result.OnHandQuantity);
        Assert.Equal(0.0000m, _repository.Balances.Single().OnHandQuantity);
    }

    [Fact]
    public async Task RecordAsync_DuplicateDocumentNoSameType_ThrowsConflictWithoutWriting()
    {
        var request = CreateRequest();
        _repository.RegisterMasterData(request);
        _repository.DocumentNos.Add((InventoryMovementType.In, request.DocumentNo));

        var exception = await Assert.ThrowsAsync<ThreeDErp.Application.Common.ConflictException>(
            () => _service.RecordInboundAsync(request));

        Assert.Contains("已存在", exception.Message);
        Assert.Equal(0, _repository.RecordMovementCallCount);
    }

    [Fact]
    public async Task RecordAsync_SameDocumentNoAcrossTypes_IsAllowed()
    {
        var request = CreateRequest();
        _repository.RegisterMasterData(request);
        _repository.DocumentNos.Add((InventoryMovementType.Out, request.DocumentNo));

        await _service.RecordInboundAsync(request);

        Assert.Single(_repository.Movements);
    }

    [Fact]
    public async Task RecordAsync_UnknownWarehouse_ThrowsNotFound()
    {
        var request = CreateRequest();
        _repository.RegisterMaterial(request.MaterialId);
        _repository.RegisterSupplier(request.SupplierId!.Value);

        var exception = await Assert.ThrowsAsync<ThreeDErp.Application.Common.NotFoundException>(
            () => _service.RecordInboundAsync(request));

        Assert.Contains("仓库", exception.Message);
        Assert.Equal(0, _repository.RecordMovementCallCount);
    }

    [Fact]
    public async Task RecordAsync_UnknownMaterial_ThrowsNotFound()
    {
        var request = CreateRequest();
        _repository.RegisterWarehouse(request.WarehouseId);
        _repository.RegisterSupplier(request.SupplierId!.Value);

        var exception = await Assert.ThrowsAsync<ThreeDErp.Application.Common.NotFoundException>(
            () => _service.RecordInboundAsync(request));

        Assert.Contains("物料", exception.Message);
    }

    [Fact]
    public async Task RecordAsync_UnknownSupplier_ThrowsNotFound()
    {
        var request = CreateRequest();
        _repository.RegisterWarehouse(request.WarehouseId);
        _repository.RegisterMaterial(request.MaterialId);

        var exception = await Assert.ThrowsAsync<ThreeDErp.Application.Common.NotFoundException>(
            () => _service.RecordInboundAsync(request));

        Assert.Contains("供应商", exception.Message);
    }

    [Fact]
    public async Task RecordOutboundAsync_UnknownCustomer_ThrowsNotFound()
    {
        var request = CreateRequest() with { DocumentNo = "SO-2026-0001", SupplierId = null, CustomerId = Guid.NewGuid() };
        _repository.RegisterWarehouse(request.WarehouseId);
        _repository.RegisterMaterial(request.MaterialId);

        var exception = await Assert.ThrowsAsync<ThreeDErp.Application.Common.NotFoundException>(
            () => _service.RecordOutboundAsync(request));

        Assert.Contains("客户", exception.Message);
    }

    [Fact]
    public async Task RecordAsync_InactiveMasterData_IsRejected()
    {
        var request = CreateRequest();
        _repository.RegisterWarehouse(request.WarehouseId, isActive: false);
        _repository.RegisterMaterial(request.MaterialId);
        _repository.RegisterSupplier(request.SupplierId!.Value);

        var exception = await Assert.ThrowsAsync<ThreeDErp.Application.Common.NotFoundException>(
            () => _service.RecordInboundAsync(request));

        Assert.Contains("仓库", exception.Message);
    }

    [Fact]
    public async Task RecordAsync_InvalidRequest_ThrowsValidationWithoutTouchingRepository()
    {
        var request = CreateRequest() with { Quantity = 0m, DocumentNo = " " };

        var exception = await Assert.ThrowsAsync<ThreeDErp.Application.Common.ValidationException>(
            () => _service.RecordInboundAsync(request));

        Assert.Contains("quantity", exception.Errors.Keys);
        Assert.Contains("documentNo", exception.Errors.Keys);
        Assert.Equal(0, _repository.RepositoryCallCount);
    }

    [Fact]
    public async Task RecordAsync_TrimsDocumentNoAndNote_BlankNoteBecomesNull()
    {
        var request = CreateRequest() with { DocumentNo = "  PO-2026-0001  ", Note = "  " };
        _repository.RegisterMasterData(request);

        var result = await _service.RecordInboundAsync(request);

        Assert.Equal("PO-2026-0001", result.DocumentNo);
        Assert.Null(result.Note);
        Assert.Equal("PO-2026-0001", _repository.Movements.Single().DocumentNo);
    }

    [Fact]
    public async Task RecordOutboundAsync_PassesAdjustedBalanceToRepository()
    {
        var request = CreateRequest() with { Quantity = 30.0000m, DocumentNo = "SO-2026-0001", SupplierId = null };
        _repository.RegisterMasterData(request);
        var balance = SeedBalance(request.WarehouseId, request.MaterialId, 50.0000m);
        _repository.Balances.Add(balance);

        await _service.RecordOutboundAsync(request);

        Assert.Equal(20.0000m, balance.OnHandQuantity);
        Assert.Equal(20.0000m, _repository.LastRecordedBalance!.OnHandQuantity);
        Assert.Same(balance, _repository.LastRecordedBalance);
    }

    /// <summary>
    /// 事务服务专用假仓储：模拟主数据、余额、单据号登记与原子落库行为。
    /// </summary>
    private sealed class FakeInventoryRepository : IInventoryRepository
    {
        public List<InventoryBalance> Balances { get; } = [];

        public List<InventoryMovement> Movements { get; } = [];

        public HashSet<(InventoryMovementType Type, string DocumentNo)> DocumentNos { get; } = [];

        public HashSet<Guid> Warehouses { get; } = [];

        public HashSet<Guid> Materials { get; } = [];

        public HashSet<Guid> Suppliers { get; } = [];

        public HashSet<Guid> Customers { get; } = [];

        public InventoryBalance? LastRecordedBalance { get; private set; }

        public int RecordMovementCallCount { get; private set; }

        public int RepositoryCallCount { get; private set; }

        public void RegisterMasterData(InventoryTransactionRequest request)
        {
            RegisterWarehouse(request.WarehouseId);
            RegisterMaterial(request.MaterialId);
            if (request.SupplierId is { } supplierId)
            {
                RegisterSupplier(supplierId);
            }

            if (request.CustomerId is { } customerId)
            {
                RegisterCustomer(customerId);
            }
        }

        public void RegisterWarehouse(Guid id, bool isActive = true)
        {
            Warehouses.Add(id);
            if (!isActive)
            {
                InactiveWarehouseIds.Add(id);
            }
        }

        public void RegisterMaterial(Guid id, bool isActive = true)
        {
            Materials.Add(id);
            if (!isActive)
            {
                InactiveMaterialIds.Add(id);
            }
        }

        public void RegisterSupplier(Guid id, bool isActive = true)
        {
            Suppliers.Add(id);
            if (!isActive)
            {
                InactiveSupplierIds.Add(id);
            }
        }

        public void RegisterCustomer(Guid id, bool isActive = true)
        {
            Customers.Add(id);
            if (!isActive)
            {
                InactiveCustomerIds.Add(id);
            }
        }

        private readonly HashSet<Guid> InactiveWarehouseIds = [];
        private readonly HashSet<Guid> InactiveMaterialIds = [];
        private readonly HashSet<Guid> InactiveSupplierIds = [];
        private readonly HashSet<Guid> InactiveCustomerIds = [];

        public Task<InventoryBalance?> FindBalanceAsync(
            Guid warehouseId,
            Guid materialId,
            CancellationToken cancellationToken = default)
        {
            RepositoryCallCount++;
            return Task.FromResult(
                Balances.FirstOrDefault(
                    balance => balance.WarehouseId == warehouseId && balance.MaterialId == materialId));
        }

        public Task<bool> DocumentNoExistsAsync(
            InventoryMovementType movementType,
            string documentNo,
            CancellationToken cancellationToken = default)
        {
            RepositoryCallCount++;
            return Task.FromResult(DocumentNos.Contains((movementType, documentNo)));
        }

        public Task<bool> WarehouseExistsAsync(Guid id, CancellationToken cancellationToken = default)
        {
            RepositoryCallCount++;
            return Task.FromResult(Warehouses.Contains(id) && !InactiveWarehouseIds.Contains(id));
        }

        public Task<bool> MaterialExistsAsync(Guid id, CancellationToken cancellationToken = default)
        {
            RepositoryCallCount++;
            return Task.FromResult(Materials.Contains(id) && !InactiveMaterialIds.Contains(id));
        }

        public Task<bool> SupplierExistsAsync(Guid id, CancellationToken cancellationToken = default)
        {
            RepositoryCallCount++;
            return Task.FromResult(Suppliers.Contains(id) && !InactiveSupplierIds.Contains(id));
        }

        public Task<bool> CustomerExistsAsync(Guid id, CancellationToken cancellationToken = default)
        {
            RepositoryCallCount++;
            return Task.FromResult(Customers.Contains(id) && !InactiveCustomerIds.Contains(id));
        }

        public Task<InventoryBalance> RecordMovementAsync(
            InventoryMovement movement,
            InventoryBalance? balance,
            CancellationToken cancellationToken = default)
        {
            RepositoryCallCount++;
            RecordMovementCallCount++;

            // 与真实仓储一致：balance 为 null 时创建新余额（入库）。
            var targetBalance = balance ?? new InventoryBalance
            {
                Id = Guid.NewGuid(),
                WarehouseId = movement.WarehouseId,
                MaterialId = movement.MaterialId,
                OnHandQuantity = movement.Quantity
            };

            Movements.Add(movement);
            DocumentNos.Add((movement.MovementType, movement.DocumentNo));
            if (targetBalance.Id == Guid.Empty || !Balances.Contains(targetBalance))
            {
                Balances.Add(targetBalance);
            }

            LastRecordedBalance = targetBalance;
            return Task.FromResult(targetBalance);
        }

        public Task<PagedResult<InventoryBalance>> FindBalancesAsync(
            int page,
            int pageSize,
            Guid? warehouseId,
            Guid? materialId,
            string? keyword,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<InventoryBalance>([], 1, pageSize, 0));

        public Task<PagedResult<InventoryAlertRule>> FindAlertRulesAsync(
            int page,
            int pageSize,
            Guid? warehouseId,
            Guid? materialId,
            bool includeDisabled,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<InventoryAlertRule>([], page, pageSize, 0));

        public Task<InventoryAlertRule?> FindAlertRuleByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<InventoryAlertRule?>(null);

        public Task<bool> AlertRuleExistsAsync(Guid warehouseId, Guid materialId, Guid? excludeId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task AddAlertRuleAsync(InventoryAlertRule rule, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateAlertRuleAsync(InventoryAlertRule rule, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAlertRuleAsync(InventoryAlertRule rule, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<PagedResult<InventoryAlertDto>> FindAlertsAsync(
            int page,
            int pageSize,
            Guid? warehouseId,
            Guid? materialId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<InventoryAlertDto>([], page, pageSize, 0));
    }
}
