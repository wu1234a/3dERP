using System.Text.RegularExpressions;

namespace ThreeDErp.Application.Materials;

/// <summary>
/// 物料创建 / 更新请求校验器。
/// 规则：Code 必填且 <=50 位大写字母数字短横线，Name 必填且 <=100，
/// Unit / MaterialType 必填且有长度上限，金额与库存必须 >=0 且符合 decimal(19,4) 精度。
/// </summary>
public sealed partial class MaterialRequestValidator
{
    public const int CodeMaxLength = 50;
    public const int NameMaxLength = 100;
    public const int SpecificationMaxLength = 200;
    public const int UnitMaxLength = 20;
    public const int MaterialTypeMaxLength = 50;

    /// <summary>decimal(19,4) 允许的最大值：15 位整数 + 4 位小数。</summary>
    public const decimal MaxAmount = 999_999_999_999_999.9999m;

    public const int AmountScale = 4;

    [GeneratedRegex("^[A-Z0-9-]+$")]
    private static partial Regex CodePattern();

    public Dictionary<string, string[]> Validate(CreateMaterialRequest request) => Validate(
        request.Code,
        request.Name,
        request.Specification,
        request.Unit,
        request.MaterialType,
        request.UnitPrice,
        request.SafetyStock);

    public Dictionary<string, string[]> Validate(UpdateMaterialRequest request) => Validate(
        request.Code,
        request.Name,
        request.Specification,
        request.Unit,
        request.MaterialType,
        request.UnitPrice,
        request.SafetyStock);

    /// <summary>
    /// 返回字段级错误集合；空集合表示校验通过。
    /// </summary>
    private static Dictionary<string, string[]> Validate(
        string? code,
        string? name,
        string? specification,
        string? unit,
        string? materialType,
        decimal unitPrice,
        decimal safetyStock)
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

        var normalizedCode = Normalize(code);
        if (normalizedCode.Length == 0)
        {
            Add("code", "物料编码必填。");
        }
        else if (normalizedCode.Length > CodeMaxLength)
        {
            Add("code", $"物料编码长度不能超过 {CodeMaxLength} 个字符。");
        }
        else if (!CodePattern().IsMatch(normalizedCode))
        {
            Add("code", "物料编码只能包含大写字母、数字和短横线。");
        }

        var normalizedName = Normalize(name);
        if (normalizedName.Length == 0)
        {
            Add("name", "物料名称必填。");
        }
        else if (normalizedName.Length > NameMaxLength)
        {
            Add("name", $"物料名称长度不能超过 {NameMaxLength} 个字符。");
        }

        if (Normalize(specification).Length > SpecificationMaxLength)
        {
            Add("specification", $"规格长度不能超过 {SpecificationMaxLength} 个字符。");
        }

        var normalizedUnit = Normalize(unit);
        if (normalizedUnit.Length == 0)
        {
            Add("unit", "计量单位必填。");
        }
        else if (normalizedUnit.Length > UnitMaxLength)
        {
            Add("unit", $"计量单位长度不能超过 {UnitMaxLength} 个字符。");
        }

        var normalizedType = Normalize(materialType);
        if (normalizedType.Length == 0)
        {
            Add("materialType", "物料类型必填。");
        }
        else if (normalizedType.Length > MaterialTypeMaxLength)
        {
            Add("materialType", $"物料类型长度不能超过 {MaterialTypeMaxLength} 个字符。");
        }

        AddAmountError(Add, "unitPrice", unitPrice, "单价");
        AddAmountError(Add, "safetyStock", safetyStock, "安全库存");

        return errors;
    }

    private static void AddAmountError(
        Action<string, string> add,
        string field,
        decimal value,
        string label)
    {
        if (value < 0)
        {
            add(field, $"{label}不能为负数。");
        }
        else if (value > MaxAmount)
        {
            add(field, $"{label}不能超过 {MaxAmount}。");
        }
        else if (decimal.Round(value, AmountScale) != value)
        {
            add(field, $"{label}最多 {AmountScale} 位小数。");
        }
    }

    /// <summary>
    /// 去除首尾空白；null 视为空字符串。
    /// </summary>
    public static string Normalize(string? value) => value?.Trim() ?? string.Empty;
}
