namespace ThreeDErp.Application.Inventory;

/// <summary>
/// 入库 / 出库事务请求校验器。
/// 规则：warehouseId / materialId 必填（非空 Guid），quantity 必须 &gt; 0、
/// 最多 4 位小数且不超过 decimal(19,4) 上限，documentNo 必填且 &lt;=64，
/// supplierId / customerId 提供时不能为空 Guid，note &lt;=512。
/// </summary>
public sealed class InventoryRequestValidator
{
    public const int DocumentNoMaxLength = 64;
    public const int NoteMaxLength = 512;
    public const int QuantityDecimalPlaces = 4;

    /// <summary>decimal(19,4) 可存储的最大值（15 位整数 + 4 位小数）。</summary>
    public const decimal MaxQuantity = 999_999_999_999_999.9999m;

    public Dictionary<string, string[]> Validate(InventoryTransactionRequest request)
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

        if (request.WarehouseId == Guid.Empty)
        {
            Add("warehouseId", "仓库 Id 必填。");
        }

        if (request.MaterialId == Guid.Empty)
        {
            Add("materialId", "物料 Id 必填。");
        }

        if (request.Quantity <= 0)
        {
            Add("quantity", "数量必须大于 0。");
        }
        else if (request.Quantity > MaxQuantity)
        {
            Add("quantity", $"数量不能超过 {MaxQuantity}。");
        }
        else if (decimal.Round(request.Quantity, QuantityDecimalPlaces) != request.Quantity)
        {
            Add("quantity", $"数量最多保留 {QuantityDecimalPlaces} 位小数。");
        }

        var documentNo = Normalize(request.DocumentNo);
        if (documentNo.Length == 0)
        {
            Add("documentNo", "单据号必填。");
        }
        else if (documentNo.Length > DocumentNoMaxLength)
        {
            Add("documentNo", $"单据号长度不能超过 {DocumentNoMaxLength} 个字符。");
        }

        if (request.SupplierId == Guid.Empty)
        {
            Add("supplierId", "供应商 Id 无效。");
        }

        if (request.CustomerId == Guid.Empty)
        {
            Add("customerId", "客户 Id 无效。");
        }

        if (Normalize(request.Note).Length > NoteMaxLength)
        {
            Add("note", $"备注长度不能超过 {NoteMaxLength} 个字符。");
        }

        return errors;
    }

    /// <summary>
    /// 去除首尾空白；null 视为空字符串。
    /// </summary>
    public static string Normalize(string? value) => value?.Trim() ?? string.Empty;
}
