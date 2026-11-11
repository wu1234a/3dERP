using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThreeDErp.Domain.Inventory;

namespace ThreeDErp.Infrastructure.Persistence.Configurations;

/// <summary>
/// InventoryBalances 表映射：WarehouseId + MaterialId 唯一索引（每仓库 × 物料一条余额）、
/// decimal(19,4) 精度、rowversion 乐观并发、指向 Warehouses / Materials 的外键。
/// </summary>
public sealed class InventoryBalanceConfiguration : IEntityTypeConfiguration<InventoryBalance>
{
    public void Configure(EntityTypeBuilder<InventoryBalance> builder)
    {
        builder.ToTable("InventoryBalances");

        builder.HasKey(balance => balance.Id);

        builder.Property(balance => balance.WarehouseId);
        builder.Property(balance => balance.MaterialId);

        builder.Property(balance => balance.OnHandQuantity)
            .HasPrecision(19, 4);

        builder.Property(balance => balance.RowVersion)
            .IsRowVersion();

        // 每个仓库 × 物料组合只有一条余额记录。
        builder.HasIndex(balance => new { balance.WarehouseId, balance.MaterialId })
            .IsUnique()
            .HasDatabaseName("IX_InventoryBalances_WarehouseId_MaterialId");

        // 仅按物料过滤（跨仓库）时的查询索引。
        builder.HasIndex(balance => balance.MaterialId)
            .HasDatabaseName("IX_InventoryBalances_MaterialId");

        // 主数据不物理删除（软删除），存在余额时禁止级联删除，保护库存数据。
        builder.HasOne(balance => balance.Warehouse)
            .WithMany()
            .HasForeignKey(balance => balance.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(balance => balance.Material)
            .WithMany()
            .HasForeignKey(balance => balance.MaterialId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
