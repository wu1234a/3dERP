using ThreeDErp.Application.Common;
using ThreeDErp.Application.Suppliers;
using ThreeDErp.Domain.Suppliers;

namespace ThreeDErp.UnitTests.Suppliers;

public sealed class SupplierServiceTests
{
    private readonly FakeSupplierRepository _repository = new();
    private readonly SupplierService _service;

    public SupplierServiceTests()
    {
        _service = new SupplierService(_repository, new SupplierRequestValidator());
    }

    private static CreateSupplierRequest CreateRequest(
        string code = "SUP-001",
        string name = "示例供应商",
        string? email = null) =>
        new(code, name, null, null, email);

    [Fact]
    public async Task CreateAsync_ValidRequest_CreatesActiveSupplier()
    {
        var dto = await _service.CreateAsync(CreateRequest(name: " 示例供应商 ", email: " user@example.com "));

        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Equal("SUP-001", dto.Code);
        Assert.Equal("示例供应商", dto.Name);
        Assert.Equal("user@example.com", dto.Email);
        Assert.True(dto.IsActive);

        var stored = Assert.Single(_repository.Suppliers);
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
        Assert.Empty(_repository.Suppliers);
    }

    [Fact]
    public async Task CreateAsync_DuplicateCode_ThrowsConflictException()
    {
        await _service.CreateAsync(CreateRequest(code: "SUP-001"));

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateAsync(CreateRequest(code: "SUP-001", name: "另一家供应商")));

        Assert.Contains("SUP-001", exception.Message);
        Assert.Single(_repository.Suppliers);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesFieldsAndKeepsId()
    {
        var created = await _service.CreateAsync(CreateRequest());

        var updated = await _service.UpdateAsync(
            created.Id,
            new UpdateSupplierRequest("SUP-001", "新名称", "李四", "13900139000", "lisi@example.com"));

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("新名称", updated.Name);
        Assert.Equal("李四", updated.ContactName);
        Assert.Equal("13900139000", updated.Phone);
        Assert.Equal("lisi@example.com", updated.Email);
        Assert.True(updated.UpdatedAtUtc >= created.UpdatedAtUtc);
    }

    [Fact]
    public async Task UpdateAsync_CodeCollisionWithOtherSupplier_ThrowsConflictException()
    {
        var first = await _service.CreateAsync(CreateRequest(code: "SUP-001"));
        await _service.CreateAsync(CreateRequest(code: "SUP-002"));

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _service.UpdateAsync(first.Id, new UpdateSupplierRequest("SUP-002", "新名称", null, null, null)));

        Assert.Contains("SUP-002", exception.Message);
    }

    [Fact]
    public async Task UpdateAsync_KeepingOwnCode_DoesNotConflict()
    {
        var created = await _service.CreateAsync(CreateRequest(code: "SUP-001"));

        var updated = await _service.UpdateAsync(
            created.Id,
            new UpdateSupplierRequest("SUP-001", "改名", null, null, null));

        Assert.Equal("SUP-001", updated.Code);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.UpdateAsync(Guid.NewGuid(), new UpdateSupplierRequest("SUP-009", "新名称", null, null, null)));
    }

    [Fact]
    public async Task DeactivateAsync_MarksSupplierInactiveWithoutDeleting()
    {
        var created = await _service.CreateAsync(CreateRequest());

        var dto = await _service.DeactivateAsync(created.Id);

        Assert.False(dto.IsActive);
        Assert.Single(_repository.Suppliers);
        Assert.False(_repository.Suppliers[0].IsActive);
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
        await _service.CreateAsync(CreateRequest(code: "SUP-001", name: "北京供应商"));
        await _service.CreateAsync(CreateRequest(code: "SUP-002", name: "上海供应商"));
        var deactivated = await _service.CreateAsync(CreateRequest(code: "SUP-003", name: "上海旧供应商"));
        await _service.DeactivateAsync(deactivated.Id);

        var active = await _service.SearchAsync(1, 10, null, includeInactive: false);
        Assert.Equal(2, active.TotalCount);

        var keyword = await _service.SearchAsync(1, 10, "上海", includeInactive: false);
        var item = Assert.Single(keyword.Items);
        Assert.Equal("SUP-002", item.Code);
        Assert.Equal(1, keyword.TotalCount);

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

    private sealed class FakeSupplierRepository : ISupplierRepository
    {
        public List<Supplier> Suppliers { get; } = [];

        public Task<PagedResult<Supplier>> FindAsync(
            int page,
            int pageSize,
            string? keyword,
            bool includeInactive,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<Supplier> query = Suppliers;

            if (!includeInactive)
            {
                query = query.Where(supplier => supplier.IsActive);
            }

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(supplier =>
                    supplier.Code.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || supplier.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || (supplier.ContactName?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            var ordered = query.OrderBy(supplier => supplier.Code).ToList();
            var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return Task.FromResult(new PagedResult<Supplier>(items, page, pageSize, ordered.Count));
        }

        public Task<Supplier?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Suppliers.FirstOrDefault(supplier => supplier.Id == id));

        public Task<bool> CodeExistsAsync(
            string code,
            Guid? excludeId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Suppliers.Any(supplier =>
                supplier.Code == code
                && (excludeId is null || supplier.Id != excludeId)));

        public Task AddAsync(Supplier supplier, CancellationToken cancellationToken = default)
        {
            Suppliers.Add(supplier);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Supplier supplier, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
