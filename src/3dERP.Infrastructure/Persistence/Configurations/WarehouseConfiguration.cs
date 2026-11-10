using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThreeDErp.Domain.Warehouses;

namespace ThreeDErp.Infrastructure.Persistence.Configurations;

/// <summary>
/// Warehouses 表映射：Code 唯一索引、字段长度限制、rowversion 乐观并发。
/// </summary>
public sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("Warehouses");

        builder.HasKey(warehouse => warehouse.Id);

        builder.Property(warehouse => warehouse.Code)
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(warehouse => warehouse.Code)
            .IsUnique()
            .HasDatabaseName("IX_Warehouses_Code");

        builder.Property(warehouse => warehouse.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(warehouse => warehouse.Address).HasMaxLength(256);
        builder.Property(warehouse => warehouse.ContactName).HasMaxLength(64);
        builder.Property(warehouse => warehouse.Phone).HasMaxLength(32);

        builder.Property(warehouse => warehouse.IsActive);
        builder.Property(warehouse => warehouse.CreatedAtUtc);
        builder.Property(warehouse => warehouse.UpdatedAtUtc);

        builder.Property(warehouse => warehouse.RowVersion)
            .IsRowVersion();
    }
}
