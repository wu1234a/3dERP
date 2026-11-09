using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThreeDErp.Domain.Customers;

namespace ThreeDErp.Infrastructure.Persistence.Configurations;

/// <summary>
/// Customers 表映射：Code 唯一索引、字段长度限制、rowversion 乐观并发。
/// </summary>
public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");

        builder.HasKey(customer => customer.Id);

        builder.Property(customer => customer.Code)
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(customer => customer.Code)
            .IsUnique()
            .HasDatabaseName("IX_Customers_Code");

        builder.Property(customer => customer.Name)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(customer => customer.ContactName).HasMaxLength(64);
        builder.Property(customer => customer.Phone).HasMaxLength(32);
        builder.Property(customer => customer.Email).HasMaxLength(128);
        builder.Property(customer => customer.Address).HasMaxLength(256);
        builder.Property(customer => customer.TaxNo).HasMaxLength(32);

        builder.Property(customer => customer.IsActive);
        builder.Property(customer => customer.CreatedAtUtc);
        builder.Property(customer => customer.UpdatedAtUtc);

        builder.Property(customer => customer.RowVersion)
            .IsRowVersion();
    }
}
