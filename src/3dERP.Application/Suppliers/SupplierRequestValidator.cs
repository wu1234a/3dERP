using System.Text.RegularExpressions;

namespace ThreeDErp.Application.Suppliers;

/// <summary>
/// 供应商创建 / 更新请求校验器。
/// 规则：Code 必填 2-32 位大写字母数字短横线，Name 必填且 <=128，
/// Email 可选但需符合格式，Phone <=32，ContactName <=64。
/// </summary>
public sealed partial class SupplierRequestValidator
{
    public const int CodeMinLength = 2;
    public const int CodeMaxLength = 32;
    public const int NameMaxLength = 128;
    public const int ContactNameMaxLength = 64;
    public const int PhoneMaxLength = 32;
    public const int EmailMaxLength = 128;

    [GeneratedRegex("^[A-Z0-9-]+$")]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$")]
    private static partial Regex EmailPattern();

    public Dictionary<string, string[]> Validate(CreateSupplierRequest request) => Validate(
        request.Code,
        request.Name,
        request.ContactName,
        request.Phone,
        request.Email);

    public Dictionary<string, string[]> Validate(UpdateSupplierRequest request) => Validate(
        request.Code,
        request.Name,
        request.ContactName,
        request.Phone,
        request.Email);

    /// <summary>
    /// 返回字段级错误集合；空集合表示校验通过。
    /// </summary>
    private static Dictionary<string, string[]> Validate(
        string? code,
        string? name,
        string? contactName,
        string? phone,
        string? email)
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
            Add("code", "供应商编码必填。");
        }
        else if (normalizedCode.Length is < CodeMinLength or > CodeMaxLength)
        {
            Add("code", $"供应商编码长度必须在 {CodeMinLength}-{CodeMaxLength} 位之间。");
        }
        else if (!CodePattern().IsMatch(normalizedCode))
        {
            Add("code", "供应商编码只能包含大写字母、数字和短横线。");
        }

        var normalizedName = Normalize(name);
        if (normalizedName.Length == 0)
        {
            Add("name", "供应商名称必填。");
        }
        else if (normalizedName.Length > NameMaxLength)
        {
            Add("name", $"供应商名称长度不能超过 {NameMaxLength} 个字符。");
        }

        if (Normalize(contactName).Length > ContactNameMaxLength)
        {
            Add("contactName", $"联系人长度不能超过 {ContactNameMaxLength} 个字符。");
        }

        if (Normalize(phone).Length > PhoneMaxLength)
        {
            Add("phone", $"电话长度不能超过 {PhoneMaxLength} 个字符。");
        }

        var normalizedEmail = Normalize(email);
        if (normalizedEmail.Length > EmailMaxLength)
        {
            Add("email", $"邮箱长度不能超过 {EmailMaxLength} 个字符。");
        }
        else if (normalizedEmail.Length > 0 && !EmailPattern().IsMatch(normalizedEmail))
        {
            Add("email", "邮箱格式不正确。");
        }

        return errors;
    }

    /// <summary>
    /// 去除首尾空白；null 视为空字符串。
    /// </summary>
    public static string Normalize(string? value) => value?.Trim() ?? string.Empty;
}
