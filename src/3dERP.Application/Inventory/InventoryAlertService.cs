using ThreeDErp.Application.Common;
using ThreeDErp.Domain.Inventory;

namespace ThreeDErp.Application.Inventory;

/// <summary>
/// 库存预警服务：预警规则的查询 / 新增 / 修改（含启停），以及基于
/// 当前余额 + 启用规则的实时预警计算。复用现有 InventoryAlertRule /
/// InventoryBalance 模型，不维护独立预警存储；预警实时计算、不落库，
/// 不存在确认 / 已读等模型没有的状态。规则变更后再次查询即生效。
/// </summary>
public sealed class InventoryAlertService(
    IInventoryAlertRepository repository,
    InventoryAlertRuleRequestValidator validator)
{
    private const int DefaultPageSize = 10;
    private const int MaxPageSize = 100;

    /// <summary>
    /// 分页查询预警规则：仓库 / 物料 / 启停精确过滤，keyword 匹配物料编码 / 名称
    /// （去除首尾空白，空白视为不过滤）。
    /// </summary>
    public async Task<PagedResult<InventoryAlertRuleDto>> SearchRulesAsync(
        int page,
        int pageSize,
        Guid? warehouseId,
        Guid? materialId,
        bool? isEnabled,
        string? keyword,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var normalizedKeyword = Normalize(keyword);
        var result = await repository.FindRulesAsync(
            page,
            pageSize,
            warehouseId,
            materialId,
            isEnabled,
            normalizedKeyword.Length == 0 ? null : normalizedKeyword,
            cancellationToken);

        return new PagedResult<InventoryAlertRuleDto>(
            [.. result.Items.Select(InventoryAlertRuleDto.FromEntity)],
            result.Page,
            result.PageSize,
            result.TotalCount);
    }

    public async Task<InventoryAlertRuleDto?> GetRuleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rule = await repository.FindRuleByIdAsync(id, cancellationToken);
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

        await EnsureScopeExistsAsync(request.WarehouseId, request.MaterialId, cancellationToken);

        // 项目约定：每个仓库 × 物料组合仅一条规则（唯一索引），重复创建返回 409。
        if (await repository.RuleExistsAsync(request.WarehouseId, request.MaterialId, null, cancellationToken))
        {
            throw new ConflictException("该仓库 × 物料组合已存在预警规则。");
        }

        var rule = new InventoryAlertRule
        {
            Id = Guid.CreateVersion7(),
            WarehouseId = request.WarehouseId,
            MaterialId = request.MaterialId,
            MinimumQuantity = request.MinimumQuantity,
            IsEnabled = request.IsEnabled
        };

        await repository.AddRuleAsync(rule, cancellationToken);
        return await LoadRuleDtoAsync(rule.Id, cancellationToken);
    }

    /// <summary>
    /// 更新规则：编辑适用范围 / 阈值，同时支持启停。范围变更与其他规则冲突时返回 409，
    /// 规则 Id 不存在时返回 404。
    /// </summary>
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

        var rule = await repository.FindRuleByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("预警规则不存在。");

        await EnsureScopeExistsAsync(request.WarehouseId, request.MaterialId, cancellationToken);

        var scopeChanged = rule.WarehouseId != request.WarehouseId || rule.MaterialId != request.MaterialId;
        if (scopeChanged
            && await repository.RuleExistsAsync(request.WarehouseId, request.MaterialId, rule.Id, cancellationToken))
        {
            throw new ConflictException("该仓库 × 物料组合已存在预警规则。");
        }

        rule.WarehouseId = request.WarehouseId;
        rule.MaterialId = request.MaterialId;
        rule.MinimumQuantity = request.MinimumQuantity;
        rule.IsEnabled = request.IsEnabled;

        await repository.UpdateRuleAsync(rule, cancellationToken);
        return await LoadRuleDtoAsync(rule.Id, cancellationToken);
    }

    /// <summary>
    /// 实时预警查询：当前余额低于启用规则下限时命中，返回库存维度、当前数量、
    /// 命中规则、阈值与预警类型。不依赖出入库流水，无命中时返回空分页集合。
    /// </summary>
    public async Task<PagedResult<InventoryAlertDto>> SearchAlertsAsync(
        int page,
        int pageSize,
        Guid? warehouseId,
        Guid? materialId,
        string? keyword,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var normalizedKeyword = Normalize(keyword);
        var result = await repository.FindAlertsAsync(
            page,
            pageSize,
            warehouseId,
            materialId,
            normalizedKeyword.Length == 0 ? null : normalizedKeyword,
            cancellationToken);

        return new PagedResult<InventoryAlertDto>(
            [.. result.Items.Select(MapAlert)],
            result.Page,
            result.PageSize,
            result.TotalCount);
    }

    private static InventoryAlertDto MapAlert(InventoryAlertRow row) => new(
        row.WarehouseId,
        row.WarehouseCode,
        row.WarehouseName,
        row.MaterialId,
        row.MaterialCode,
        row.MaterialName,
        row.MaterialUnit,
        row.OnHandQuantity,
        row.RuleId,
        row.MinimumQuantity,
        InventoryAlertTypes.LowStock);

    /// <summary>规则引用的仓库 / 物料必须真实存在，否则视为无效标识（404）。</summary>
    private async Task EnsureScopeExistsAsync(
        Guid warehouseId,
        Guid materialId,
        CancellationToken cancellationToken)
    {
        if (!await repository.WarehouseExistsAsync(warehouseId, cancellationToken))
        {
            throw new NotFoundException("仓库不存在或已被删除。");
        }

        if (!await repository.MaterialExistsAsync(materialId, cancellationToken))
        {
            throw new NotFoundException("物料不存在或已被删除。");
        }
    }

    /// <summary>
    /// 写入后按 Id 重新加载规则（含主数据导航属性），保证 DTO 反映持久化结果。
    /// </summary>
    private async Task<InventoryAlertRuleDto> LoadRuleDtoAsync(Guid id, CancellationToken cancellationToken)
    {
        var rule = await repository.FindRuleByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("预警规则不存在。");
        return InventoryAlertRuleDto.FromEntity(rule);
    }

    /// <summary>去除首尾空白；null 视为空字符串。</summary>
    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
}
