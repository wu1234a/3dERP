using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThreeDErp.Domain.Materials;

namespace ThreeDErp.Infrastructure.Persistence.Configurations;

/// <summary>
/// Materials 表映射：Code 唯一索引、字段长度限制、decimal(19,4) 精度、rowversion 乐观并发。
/// </summary>
public sealed class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    public void Configure(EntityTypeBuilder<Material> builder)
    {
        builder.ToTable("Materials");

        builder.HasKey(material => material.Id);

        builder.Property(material => material.Code)
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(material => material.Code)
            .IsUnique()
            .HasDatabaseName("IX_Materials_Code");

        builder.Property(material => material.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(material => material.Specification).HasMaxLength(200);

        builder.Property(material => material.Unit)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(material => material.MaterialType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(material => material.UnitPrice)
            .HasPrecision(19, 4);

        builder.Property(material => material.SafetyStock)
            .HasPrecision(19, 4);

        builder.Property(material => material.IsActive);
        builder.Property(material => material.CreatedAtUtc);
        builder.Property(material => material.UpdatedAtUtc);

        builder.Property(material => material.RowVersion)
            .IsRowVersion();
    }
}
