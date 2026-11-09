namespace ThreeDErp.Domain.Materials;

/// <summary>
/// 物料主数据。涵盖树脂、金属粉末、线材等 3D 打印耗材，
/// 结构与 Customer 保持一致，作为主数据模块的第二个实体。
/// </summary>
public sealed class Material
{
    public Guid Id { get; set; }

    /// <summary>业务编码，全局唯一，最多 50 位大写字母 / 数字 / 短横线。</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>物料名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>规格型号，如 “SLA 通用树脂 1kg 装”。</summary>
    public string? Specification { get; set; }

    /// <summary>计量单位，如 kg、L、个。</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>物料类型，如 树脂、金属粉末、线材。</summary>
    public string MaterialType { get; set; } = string.Empty;

    /// <summary>参考单价，decimal(19,4)。</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>安全库存下限，decimal(19,4)。</summary>
    public decimal SafetyStock { get; set; }

    /// <summary>停用标记。停用（软删除）后不再出现在默认列表中，不做物理删除。</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion，乐观并发控制。</summary>
    public byte[] RowVersion { get; set; } = [];
}
