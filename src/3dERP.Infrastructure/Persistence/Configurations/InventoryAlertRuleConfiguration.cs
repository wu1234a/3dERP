using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThreeDErp.Domain.Inventory;

namespace ThreeDErp.Infrastructure.Persistence.Configurations;

/// <summary>
/// InventoryAlertRules 表映射：WarehouseId + MaterialId 唯一索引（每仓库 × 物料一条规则）、
/// decimal(19,4) 精度、指向 Warehouses / Materials 的外键。
/// </summary>
public sealed class InventoryAlertRuleConfiguration : IEntityTypeConfiguration<InventoryAlertRule>
{
    public void Configure(EntityTypeBuilder<InventoryAlertRule> builder)
    {
        builder.ToTable("InventoryAlertRules");

        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.WarehouseId);
        builder.Property(rule => rule.MaterialId);

        builder.Property(rule => rule.MinimumQuantity)
            .HasPrecision(19, 4);

        builder.Property(rule => rule.IsEnabled);

        // 每个仓库 × 物料组合只有一条预警规则。
        builder.HasIndex(rule => new { rule.WarehouseId, rule.MaterialId })
            .IsUnique()
            .HasDatabaseName("IX_InventoryAlertRules_WarehouseId_MaterialId");

        // 主数据不物理删除（软删除），存在规则时禁止级联删除。
        builder.HasOne(rule => rule.Warehouse)
            .WithMany()
            .HasForeignKey(rule => rule.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(rule => rule.Material)
            .WithMany()
            .HasForeignKey(rule => rule.MaterialId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
