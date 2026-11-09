namespace ThreeDErp.Domain.Customers;

/// <summary>
/// 客户主数据。作为后续物料、仓库等主数据的样板实体。
/// </summary>
public sealed class Customer
{
    public Guid Id { get; set; }

    /// <summary>业务编码，全局唯一，2-32 位大写字母 / 数字 / 短横线。</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>客户名称。</summary>
    public string Name { get; set; } = string.Empty;

    public string? ContactName { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Address { get; set; }

    /// <summary>税号。</summary>
    public string? TaxNo { get; set; }

    /// <summary>停用标记。停用（软删除）后不再出现在默认列表中，不做物理删除。</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion，乐观并发控制。</summary>
    public byte[] RowVersion { get; set; } = [];
}
