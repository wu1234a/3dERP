using ThreeDErp.Application.Common;
using ThreeDErp.Application.Materials;
using ThreeDErp.Application.Warehouses;
using ThreeDErp.Domain.Inventory;
using ThreeDErp.Domain.Materials;
using ThreeDErp.Domain.Warehouses;

namespace ThreeDErp.Application.Inventory;

/// <summary>
/// 库存预警服务：预警规则的增删改查 + 低于预警下限的余额查询。
/// 校验（请求字段、仓库 / 物料存在、仓库 × 物料唯一）集中在这里，
/// 不放在 API endpoint 中。
/// </summary>
public sealed class InventoryAlertService(
    IInventoryRepository repository,
    IWarehouseRepository warehouseRepository,
    IMaterialRepository materialRepository,
    InventoryAlertRuleRequestValidator validator)
{
    private const int DefaultPageSize = 10;
    private const int MaxPageSize = 100;

    /// <summary>
    /// 分页查询预警规则；默认仅返回启用中的规则，includeDisabled 为 true 时包含停用规则。
    /// </summary>
    public async Task<PagedResult<InventoryAlertRuleDto>> SearchRulesAsync(
        int page,
        int pageSize,
        Guid? warehouseId,
        Guid? materialId,
        bool includeDisabled,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var result = await repository.FindAlertRulesAsync(
            page,
            pageSize,
            warehouseId,
            materialId,
            includeDisabled,
            cancellationToken);

        return new PagedResult<InventoryAlertRuleDto>(
            [.. result.Items.Select(InventoryAlertRuleDto.FromEntity)],
            result.Page,
            result.PageSize,
            result.TotalCount);
    }

    public async Task<InventoryAlertRuleDto?> GetRuleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rule = await repository.FindAlertRuleByIdAsync(id, cancellationToken);
        return rule is null ? null : InventoryAlertRuleDto.FromEntity(rule);
    }

    public async Task<InventoryAlertRuleDto> CreateRuleAsync(
        CreateInventoryAlertRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = validator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var warehouse = await RequireWarehouseAsync(request.WarehouseId, cancellationToken);
        var material = await RequireMaterialAsync(request.MaterialId, cancellationToken);

        if (await repository.AlertRuleExistsAsync(warehouse.Id, material.Id, null, cancellationToken))
        {
            throw new ConflictException(
                $"仓库 {warehouse.Code} 与物料 {material.Code} 的预警规则已存在。");
        }

        var rule = new InventoryAlertRule
        {
            Id = Guid.CreateVersion7(),
            WarehouseId = warehouse.Id,
            MaterialId = material.Id,
            MinimumQuantity = request.MinimumQuantity,
            IsEnabled = request.IsEnabled
        };

        await repository.AddAlertRuleAsync(rule, cancellationToken);
        return InventoryAlertRuleDto.From(rule, warehouse, material);
    }

    public async Task<InventoryAlertRuleDto> UpdateRuleAsync(
        Guid id,
        UpdateInventoryAlertRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = validator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var rule = await repository.FindAlertRuleByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("预警规则不存在或已被删除。");

        var warehouse = await RequireWarehouseAsync(request.WarehouseId, cancellationToken);
        var material = await RequireMaterialAsync(request.MaterialId, cancellationToken);

        var pairChanged = rule.WarehouseId != warehouse.Id || rule.MaterialId != material.Id;
        if (pairChanged
            && await repository.AlertRuleExistsAsync(warehouse.Id, material.Id, rule.Id, cancellationToken))
        {
            throw new ConflictException(
                $"仓库 {warehouse.Code} 与物料 {material.Code} 的预警规则已存在。");
        }

        rule.WarehouseId = warehouse.Id;
        rule.MaterialId = material.Id;
        rule.MinimumQuantity = request.MinimumQuantity;
        rule.IsEnabled = request.IsEnabled;

        await repository.UpdateAlertRuleAsync(rule, cancellationToken);
        return InventoryAlertRuleDto.From(rule, warehouse, material);
    }

    /// <summary>删除预警规则。预警规则是配置数据而非业务单据，允许物理删除。</summary>
    public async Task DeleteRuleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rule = await repository.FindAlertRuleByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("预警规则不存在或已被删除。");

        await repository.DeleteAlertRuleAsync(rule, cancellationToken);
    }

    /// <summary>
    /// 分页查询库存预警：启用中的规则，余额（无余额记录视为 0）低于 MinimumQuantity 时返回。
    /// </summary>
    public Task<PagedResult<InventoryAlertDto>> SearchAlertsAsync(
        int page,
        int pageSize,
        Guid? warehouseId,
        Guid? materialId,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        return repository.FindAlertsAsync(page, pageSize, warehouseId, materialId, cancellationToken);
    }

    private async Task<Warehouse> RequireWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken) =>
        await warehouseRepository.FindByIdAsync(warehouseId, cancellationToken)
            ?? throw new NotFoundException("仓库不存在或已被删除。");

    private async Task<Material> RequireMaterialAsync(Guid materialId, CancellationToken cancellationToken) =>
        await materialRepository.FindByIdAsync(materialId, cancellationToken)
            ?? throw new NotFoundException("物料不存在或已被删除。");
}
