using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThreeDErp.Domain.Inventory;

namespace ThreeDErp.Infrastructure.Persistence.Configurations;

/// <summary>
/// InventoryMovements 表映射：MovementType + DocumentNo 唯一索引（防重复记账）、
/// 仓库 × 物料 × 发生时间流水索引、decimal(19,4) 精度、
/// 指向 Warehouses / Materials / Suppliers / Customers 的外键。
/// </summary>
public sealed class InventoryMovementConfiguration : IEntityTypeConfiguration<InventoryMovement>
{
    public const int DocumentNoMaxLength = 64;
    public const int RemarkMaxLength = 512;

    public void Configure(EntityTypeBuilder<InventoryMovement> builder)
    {
        builder.ToTable("InventoryMovements");

        builder.HasKey(movement => movement.Id);

        builder.Property(movement => movement.MovementType)
            .HasConversion<int>();

        builder.Property(movement => movement.DocumentNo)
            .HasMaxLength(DocumentNoMaxLength)
            .IsRequired();

        builder.Property(movement => movement.WarehouseId);
        builder.Property(movement => movement.MaterialId);

        builder.Property(movement => movement.Quantity)
            .HasPrecision(19, 4);

        builder.Property(movement => movement.SupplierId);
        builder.Property(movement => movement.CustomerId);

        builder.Property(movement => movement.OccurredAtUtc);
        builder.Property(movement => movement.Remark).HasMaxLength(RemarkMaxLength);

        // 同一移动类型下单据号唯一，防止同一单据重复记账。
        builder.HasIndex(movement => new { movement.MovementType, movement.DocumentNo })
            .IsUnique()
            .HasDatabaseName("IX_InventoryMovements_MovementType_DocumentNo");

        // 仓库 × 物料 × 发生时间的流水查询（余额重建、预警计算）。
        builder.HasIndex(movement => new { movement.WarehouseId, movement.MaterialId, movement.OccurredAtUtc })
            .HasDatabaseName("IX_InventoryMovements_WarehouseId_MaterialId_OccurredAtUtc");

        // 往来单位维度的追溯查询（外键列索引）。
        builder.HasIndex(movement => movement.SupplierId)
            .HasDatabaseName("IX_InventoryMovements_SupplierId");

        builder.HasIndex(movement => movement.CustomerId)
            .HasDatabaseName("IX_InventoryMovements_CustomerId");

        // 流水是不可变历史，禁止通过主数据删除级联清理。
        builder.HasOne(movement => movement.Warehouse)
            .WithMany()
            .HasForeignKey(movement => movement.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(movement => movement.Material)
            .WithMany()
            .HasForeignKey(movement => movement.MaterialId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(movement => movement.Supplier)
            .WithMany()
            .HasForeignKey(movement => movement.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(movement => movement.Customer)
            .WithMany()
            .HasForeignKey(movement => movement.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
