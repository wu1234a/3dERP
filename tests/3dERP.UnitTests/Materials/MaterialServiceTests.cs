using ThreeDErp.Application.Common;
using ThreeDErp.Application.Materials;
using ThreeDErp.Domain.Materials;

namespace ThreeDErp.UnitTests.Materials;

public sealed class MaterialServiceTests
{
    private readonly FakeMaterialRepository _repository = new();
    private readonly MaterialService _service;

    public MaterialServiceTests()
    {
        _service = new MaterialService(_repository, new MaterialRequestValidator());
    }

    private static CreateMaterialRequest CreateRequest(
        string code = "MAT-RESIN-001",
        string name = "光敏树脂",
        string? specification = null,
        string unit = "kg",
        string materialType = "树脂",
        decimal unitPrice = 350.0000m,
        decimal safetyStock = 10.0000m) =>
        new(code, name, specification, unit, materialType, unitPrice, safetyStock);

    [Fact]
    public async Task CreateAsync_ValidRequest_CreatesActiveMaterial()
    {
        var dto = await _service.CreateAsync(CreateRequest(
            name: " 光敏树脂 ",
            specification: " SLA 通用树脂 1kg 装 ",
            unitPrice: 350.5m,
            safetyStock: 10.25m));

        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Equal("MAT-RESIN-001", dto.Code);
        Assert.Equal("光敏树脂", dto.Name);
        Assert.Equal("SLA 通用树脂 1kg 装", dto.Specification);
        Assert.Equal("kg", dto.Unit);
        Assert.Equal("树脂", dto.MaterialType);
        Assert.Equal(350.5m, dto.UnitPrice);
        Assert.Equal(10.25m, dto.SafetyStock);
        Assert.True(dto.IsActive);

        var stored = Assert.Single(_repository.Materials);
        Assert.Equal(dto.Id, stored.Id);
        Assert.Equal(stored.CreatedAtUtc, stored.UpdatedAtUtc);
    }

    [Fact]
    public async Task CreateAsync_BlankSpecification_IsStoredAsNull()
    {
        var dto = await _service.CreateAsync(CreateRequest(specification: "   "));

        Assert.Null(dto.Specification);
    }

    [Fact]
    public async Task CreateAsync_InvalidRequest_ThrowsValidationExceptionWithoutSaving()
    {
        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateAsync(CreateRequest(code: "abc")));

        Assert.Contains("code", exception.Errors.Keys);
        Assert.Empty(_repository.Materials);
    }

    [Fact]
    public async Task CreateAsync_DuplicateCode_ThrowsConflictException()
    {
        await _service.CreateAsync(CreateRequest(code: "MAT-RESIN-001"));

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateAsync(CreateRequest(code: "MAT-RESIN-001", name: "另一种树脂")));

        Assert.Contains("MAT-RESIN-001", exception.Message);
        Assert.Single(_repository.Materials);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesFieldsAndKeepsId()
    {
        var created = await _service.CreateAsync(CreateRequest());

        var updated = await _service.UpdateAsync(
            created.Id,
            new UpdateMaterialRequest("MAT-RESIN-001", "高强度树脂", "工业级", "g", "树脂", 420.0000m, 25.5000m));

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("高强度树脂", updated.Name);
        Assert.Equal("工业级", updated.Specification);
        Assert.Equal("g", updated.Unit);
        Assert.Equal(420.0000m, updated.UnitPrice);
        Assert.Equal(25.5000m, updated.SafetyStock);
        Assert.True(updated.UpdatedAtUtc >= created.UpdatedAtUtc);
    }

    [Fact]
    public async Task UpdateAsync_CodeCollisionWithOtherMaterial_ThrowsConflictException()
    {
        var first = await _service.CreateAsync(CreateRequest(code: "MAT-RESIN-001"));
        await _service.CreateAsync(CreateRequest(code: "MAT-POWDER-001", materialType: "金属粉末"));

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _service.UpdateAsync(first.Id, new UpdateMaterialRequest(
                "MAT-POWDER-001", "高强度树脂", null, "kg", "树脂", 420.0000m, 25.5000m)));

        Assert.Contains("MAT-POWDER-001", exception.Message);
    }

    [Fact]
    public async Task UpdateAsync_KeepingOwnCode_DoesNotConflict()
    {
        var created = await _service.CreateAsync(CreateRequest(code: "MAT-RESIN-001"));

        var updated = await _service.UpdateAsync(
            created.Id,
            new UpdateMaterialRequest("MAT-RESIN-001", "改名树脂", null, "kg", "树脂", 1m, 2m));

        Assert.Equal("MAT-RESIN-001", updated.Code);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.UpdateAsync(
                Guid.NewGuid(),
                new UpdateMaterialRequest("MAT-RESIN-009", "新树脂", null, "kg", "树脂", 1m, 2m)));
    }

    [Fact]
    public async Task DeactivateAsync_MarksMaterialInactiveWithoutDeleting()
    {
        var created = await _service.CreateAsync(CreateRequest());

        var dto = await _service.DeactivateAsync(created.Id);

        Assert.False(dto.IsActive);
        Assert.Single(_repository.Materials);
        Assert.False(_repository.Materials[0].IsActive);
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
        await _service.CreateAsync(CreateRequest(code: "MAT-RESIN-001", name: "光敏树脂"));
        await _service.CreateAsync(CreateRequest(code: "MAT-POWDER-001", name: "钛合金粉末", materialType: "金属粉末"));
        var deactivated = await _service.CreateAsync(CreateRequest(code: "MAT-RESIN-002", name: "旧款树脂"));
        await _service.DeactivateAsync(deactivated.Id);

        var active = await _service.SearchAsync(1, 10, null, includeInactive: false);
        Assert.Equal(2, active.TotalCount);

        // 关键字命中编码
        var byCode = await _service.SearchAsync(1, 10, "POWDER", includeInactive: false);
        var codeItem = Assert.Single(byCode.Items);
        Assert.Equal("MAT-POWDER-001", codeItem.Code);

        // 关键字命中名称
        var byName = await _service.SearchAsync(1, 10, "树脂", includeInactive: false);
        var nameItem = Assert.Single(byName.Items);
        Assert.Equal("MAT-RESIN-001", nameItem.Code);

        // 关键字命中物料类型
        var byType = await _service.SearchAsync(1, 10, "金属", includeInactive: true);
        var typeItem = Assert.Single(byType.Items);
        Assert.Equal("MAT-POWDER-001", typeItem.Code);

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

    private sealed class FakeMaterialRepository : IMaterialRepository
    {
        public List<Material> Materials { get; } = [];

        public Task<PagedResult<Material>> FindAsync(
            int page,
            int pageSize,
            string? keyword,
            bool includeInactive,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<Material> query = Materials;

            if (!includeInactive)
            {
                query = query.Where(material => material.IsActive);
            }

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(material =>
                    material.Code.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || material.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || material.MaterialType.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || (material.Specification?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            var ordered = query.OrderBy(material => material.Code).ToList();
            var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return Task.FromResult(new PagedResult<Material>(items, page, pageSize, ordered.Count));
        }

        public Task<Material?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Materials.FirstOrDefault(material => material.Id == id));

        public Task<bool> CodeExistsAsync(
            string code,
            Guid? excludeId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Materials.Any(material =>
                material.Code == code
                && (excludeId is null || material.Id != excludeId)));

        public Task AddAsync(Material material, CancellationToken cancellationToken = default)
        {
            Materials.Add(material);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Material material, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
