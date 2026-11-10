namespace ThreeDErp.Domain.Warehouses;

/// <summary>
/// 仓库主数据。与供应商/客户主数据保持相同的分层和实现模式。
/// </summary>
public sealed class Warehouse
{
    public Guid Id { get; set; }

    /// <summary>业务编码，全局唯一。</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>仓库名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>仓库地址。</summary>
    public string? Address { get; set; }

    public string? ContactName { get; set; }

    public string? Phone { get; set; }

    /// <summary>停用标记。停用（软删除）后不再出现在默认列表中，不做物理删除。</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>SQL Server rowversion，乐观并发控制。</summary>
    public byte[] RowVersion { get; set; } = [];
}
