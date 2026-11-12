namespace ThreeDErp.Application.Inventory;

/// <summary>
/// 库存预警规则创建 / 更新请求校验器。
/// 规则：仓库 / 物料必填（非空 Guid），MinimumQuantity >= 0 且符合 decimal(19,4) 精度。
/// 仓库 / 物料是否存在、仓库 × 物料组合是否唯一由 InventoryAlertService 校验。
/// </summary>
public sealed class InventoryAlertRuleRequestValidator
{
    /// <summary>decimal(19,4) 允许的最大值：15 位整数 + 4 位小数。</summary>
    public const decimal MaxQuantity = 999_999_999_999_999.9999m;

    public const int QuantityScale = 4;

    public Dictionary<string, string[]> Validate(CreateInventoryAlertRuleRequest request) => Validate(
        request.WarehouseId,
        request.MaterialId,
        request.MinimumQuantity);

    public Dictionary<string, string[]> Validate(UpdateInventoryAlertRuleRequest request) => Validate(
        request.WarehouseId,
        request.MaterialId,
        request.MinimumQuantity);

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
            Add("warehouseId", "仓库必填。");
        }

        if (materialId == Guid.Empty)
        {
            Add("materialId", "物料必填。");
        }

        AddQuantityError(Add, "minimumQuantity", minimumQuantity);

        return errors;
    }

    private static void AddQuantityError(Action<string, string> add, string field, decimal value)
    {
        if (value < 0)
        {
            add(field, "预警下限不能为负数。");
        }
        else if (value > MaxQuantity)
        {
            add(field, $"预警下限不能超过 {MaxQuantity}。");
        }
        else if (decimal.Round(value, QuantityScale) != value)
        {
            add(field, $"预警下限最多 {QuantityScale} 位小数。");
        }
    }
}
