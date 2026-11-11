namespace ThreeDErp.Application.Inventory;

/// <summary>
/// 创建库存预警规则请求。仅包含现有模型支持的字段：适用范围（仓库 / 物料）、
/// 阈值（预警下限）与启停标志；未显式指定时规则默认启用。
/// </summary>
public sealed record CreateInventoryAlertRuleRequest(
    Guid WarehouseId,
    Guid MaterialId,
    decimal MinimumQuantity,
    bool IsEnabled = true);

/// <summary>
/// 更新库存预警规则请求：编辑适用范围 / 阈值，同时支持启停（IsEnabled）。
/// </summary>
public sealed record UpdateInventoryAlertRuleRequest(
    Guid WarehouseId,
    Guid MaterialId,
    decimal MinimumQuantity,
    bool IsEnabled);
