namespace ThreeDErp.Application.Inventory;

/// <summary>
/// 创建库存预警规则请求。
/// </summary>
public sealed record CreateInventoryAlertRuleRequest(
    Guid WarehouseId,
    Guid MaterialId,
    decimal MinimumQuantity,
    bool IsEnabled = true);

/// <summary>
/// 更新库存预警规则请求（全量字段）。
/// </summary>
public sealed record UpdateInventoryAlertRuleRequest(
    Guid WarehouseId,
    Guid MaterialId,
    decimal MinimumQuantity,
    bool IsEnabled);
