using ThreeDErp.Application.Common;
using ThreeDErp.Application.Warehouses;
using ThreeDErp.Domain.Warehouses;

namespace ThreeDErp.UnitTests.Warehouses;

public sealed class WarehouseServiceTests
{
    private readonly FakeWarehouseRepository _repository = new();
    private readonly WarehouseService _service;

    public WarehouseServiceTests()
    {
        _service = new WarehouseService(_repository, new WarehouseRequestValidator());
    }

    private static CreateWarehouseRequest CreateRequest(
        string code = "WH-001",
        string name = "示例仓库",
        string? address = null) =>
        new(code, name, address, null, null);

    [Fact]
    public async Task CreateAsync_ValidRequest_CreatesActiveWarehouse()
    {
        var dto = await _service.CreateAsync(CreateRequest(name: " 示例仓库 ", address: " 上海市浦东新区 "));

        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Equal("WH-001", dto.Code);
        Assert.Equal("示例仓库", dto.Name);
        Assert.Equal("上海市浦东新区", dto.Address);
        Assert.True(dto.IsActive);

        var stored = Assert.Single(_repository.Warehouses);
        Assert.Equal(dto.Id, stored.Id);
        Assert.Equal(dto.Code, stored.Code);
        Assert.Equal(stored.CreatedAtUtc, stored.UpdatedAtUtc);
    }

    [Fact]
    public async Task CreateAsync_InvalidRequest_ThrowsValidationExceptionWithoutSaving()
    {
        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateAsync(CreateRequest(code: "abc")));

        Assert.Contains("code", exception.Errors.Keys);
        Assert.Empty(_repository.Warehouses);
    }

    [Fact]
    public async Task CreateAsync_DuplicateCode_ThrowsConflictException()
    {
        await _service.CreateAsync(CreateRequest(code: "WH-001"));

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateAsync(CreateRequest(code: "WH-001", name: "另一个仓库")));

        Assert.Contains("WH-001", exception.Message);
        Assert.Single(_repository.Warehouses);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesFieldsAndKeepsId()
    {
        var created = await _service.CreateAsync(CreateRequest());

        var updated = await _service.UpdateAsync(
            created.Id,
            new UpdateWarehouseRequest("WH-001", "新名称", "北京市海淀区仓库路 8 号", "李四", "13900139000"));

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("新名称", updated.Name);
        Assert.Equal("北京市海淀区仓库路 8 号", updated.Address);
        Assert.Equal("李四", updated.ContactName);
        Assert.Equal("13900139000", updated.Phone);
        Assert.True(updated.UpdatedAtUtc >= created.UpdatedAtUtc);
    }

    [Fact]
    public async Task UpdateAsync_CodeCollisionWithOtherWarehouse_ThrowsConflictException()
    {
        var first = await _service.CreateAsync(CreateRequest(code: "WH-001"));
        await _service.CreateAsync(CreateRequest(code: "WH-002"));

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _service.UpdateAsync(first.Id, new UpdateWarehouseRequest("WH-002", "新名称", null, null, null)));

        Assert.Contains("WH-002", exception.Message);
    }

    [Fact]
    public async Task UpdateAsync_KeepingOwnCode_DoesNotConflict()
    {
        var created = await _service.CreateAsync(CreateRequest(code: "WH-001"));

        var updated = await _service.UpdateAsync(
            created.Id,
            new UpdateWarehouseRequest("WH-001", "改名", null, null, null));

        Assert.Equal("WH-001", updated.Code);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.UpdateAsync(Guid.NewGuid(), new UpdateWarehouseRequest("WH-009", "新名称", null, null, null)));
    }

    [Fact]
    public async Task DeactivateAsync_MarksWarehouseInactiveWithoutDeleting()
    {
        var created = await _service.CreateAsync(CreateRequest());

        var dto = await _service.DeactivateAsync(created.Id);

        Assert.False(dto.IsActive);
        Assert.Single(_repository.Warehouses);
        Assert.False(_repository.Warehouses[0].IsActive);
    }

    [Fact]
    public async Task DeactivateAsync_IsIdempotent()
    {
        var created = await _service.CreateAsync(CreateRequest());

        await _service.DeactivateAsync(created.Id);
        var dto = await _service.DeactivateAsync(created.Id);

        Assert.False(dto.IsActive);
    }

    [Fact]
    public async Task DeactivateAsync_UnknownId_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.DeactivateAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task SearchAsync_FiltersByKeywordAndActiveFlag()
    {
        await _service.CreateAsync(CreateRequest(code: "WH-001", name: "北京一号仓", address: "北京市朝阳区"));
        await _service.CreateAsync(CreateRequest(code: "WH-002", name: "上海一号仓", address: "上海市浦东新区"));
        var deactivated = await _service.CreateAsync(CreateRequest(code: "WH-003", name: "上海旧仓", address: "上海市宝山区"));
        await _service.DeactivateAsync(deactivated.Id);

        var active = await _service.SearchAsync(1, 10, null, includeInactive: false);
        Assert.Equal(2, active.TotalCount);

        var byName = await _service.SearchAsync(1, 10, "上海", includeInactive: false);
        var nameItem = Assert.Single(byName.Items);
        Assert.Equal("WH-002", nameItem.Code);
        Assert.Equal(1, byName.TotalCount);

        var byAddress = await _service.SearchAsync(1, 10, "宝山", includeInactive: true);
        var addressItem = Assert.Single(byAddress.Items);
        Assert.Equal("WH-003", addressItem.Code);

        var all = await _service.SearchAsync(1, 10, null, includeInactive: true);
        Assert.Equal(3, all.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_ReturnsPagedResultWithClampedPaging()
    {
        var result = await _service.SearchAsync(page: 0, pageSize: 500, keyword: "  ", includeInactive: false);

        Assert.Equal(1, result.Page);
        Assert.Equal(100, result.PageSize);
        Assert.Empty(result.Items);
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
            CancellationToken cancellationToken = default)
        {
            IEnumerable<Warehouse> query = Warehouses;

            if (!includeInactive)
            {
                query = query.Where(warehouse => warehouse.IsActive);
            }

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(warehouse =>
                    warehouse.Code.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || warehouse.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || (warehouse.Address?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            var ordered = query.OrderBy(warehouse => warehouse.Code).ToList();
            var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return Task.FromResult(new PagedResult<Warehouse>(items, page, pageSize, ordered.Count));
        }

        public Task<Warehouse?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Warehouses.FirstOrDefault(warehouse => warehouse.Id == id));

        public Task<bool> CodeExistsAsync(
            string code,
            Guid? excludeId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Warehouses.Any(warehouse =>
                warehouse.Code == code
                && (excludeId is null || warehouse.Id != excludeId)));

        public Task AddAsync(Warehouse warehouse, CancellationToken cancellationToken = default)
        {
            Warehouses.Add(warehouse);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Warehouse warehouse, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
