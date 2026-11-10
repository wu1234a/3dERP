using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThreeDErp.Domain.Suppliers;

namespace ThreeDErp.Infrastructure.Persistence.Configurations;

/// <summary>
/// Suppliers 表映射：Code 唯一索引、字段长度限制、rowversion 乐观并发。
/// </summary>
public sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers");

        builder.HasKey(supplier => supplier.Id);

        builder.Property(supplier => supplier.Code)
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(supplier => supplier.Code)
            .IsUnique()
            .HasDatabaseName("IX_Suppliers_Code");

        builder.Property(supplier => supplier.Name)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(supplier => supplier.ContactName).HasMaxLength(64);
        builder.Property(supplier => supplier.Phone).HasMaxLength(32);
        builder.Property(supplier => supplier.Email).HasMaxLength(128);

        builder.Property(supplier => supplier.IsActive);
        builder.Property(supplier => supplier.CreatedAtUtc);
        builder.Property(supplier => supplier.UpdatedAtUtc);

        builder.Property(supplier => supplier.RowVersion)
            .IsRowVersion();
    }
}
