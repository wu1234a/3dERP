namespace ThreeDErp.Application.Inventory;

/// <summary>
/// 预警规则创建 / 更新请求校验器。
/// 规则：仓库 / 物料标识必填（非空 Guid），预警下限必须为非负数，
/// 且取值须落在 decimal(19,4) 的上下限内（现有模型只有下限阈值一个数字字段，
/// 因此"上下限关系"即 0 &lt;= MinimumQuantity &lt;= decimal(19,4) 最大值，最多 4 位小数）。
/// </summary>
public sealed class InventoryAlertRuleRequestValidator
{
    /// <summary>decimal(19,4) 允许的最大值：15 位整数 + 4 位小数。</summary>
    public const decimal MaxQuantity = 999_999_999_999_999.9999m;

    public const int QuantityScale = 4;

    public Dictionary<string, string[]> Validate(CreateInventoryAlertRuleRequest request) =>
        Validate(request.WarehouseId, request.MaterialId, request.MinimumQuantity);

    public Dictionary<string, string[]> Validate(UpdateInventoryAlertRuleRequest request) =>
        Validate(request.WarehouseId, request.MaterialId, request.MinimumQuantity);

    /// <summary>
    /// 返回字段级错误集合；空集合表示校验通过。
    /// </summary>
    private static Dictionary<string, string[]> Validate(
        Guid warehouseId,
        Guid materialId,
        decimal minimumQuantity)
    {
        var errors = new Dictionary<string, string[]>();

        void Add(string field, string message)
        {
            if (errors.TryGetValue(field, out var existing))
            {
                errors[field] = [.. existing, message];
            }
            else
            {
                errors[field] = [message];
            }
        }

        if (warehouseId == Guid.Empty)
        {
            Add("warehouseId", "仓库 Id 必填。");
        }

        if (materialId == Guid.Empty)
        {
            Add("materialId", "物料 Id 必填。");
        }

        if (minimumQuantity < 0)
        {
            Add("minimumQuantity", "预警下限不能为负数。");
        }
        else if (minimumQuantity > MaxQuantity)
        {
            Add("minimumQuantity", $"预警下限不能超过 {MaxQuantity}。");
        }
        else if (decimal.Round(minimumQuantity, QuantityScale) != minimumQuantity)
        {
            Add("minimumQuantity", $"预警下限最多 {QuantityScale} 位小数。");
        }

        return errors;
    }
}
