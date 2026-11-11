using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ThreeDErp.Domain.Customers;
using ThreeDErp.Domain.Inventory;
using ThreeDErp.Domain.Materials;
using ThreeDErp.Domain.Suppliers;
using ThreeDErp.Domain.Warehouses;
using ThreeDErp.Infrastructure.Persistence;

namespace ThreeDErp.IntegrationTests.Inventory;

/// <summary>
/// 库存模型约束测试：用 SQL Server 提供商构建 ErpDbContext 模型（仅构建模型，不连接数据库），
/// 验证 decimal(19,4) 精度、rowversion 并发令牌、WarehouseId + MaterialId /
/// MovementType + DocumentNo 唯一索引，以及到 Material / Warehouse / Supplier / Customer
/// 主数据的外键与删除行为。
/// </summary>
public sealed class InventoryModelConstraintTests
{
    private static readonly Lazy<IModel> _model = new(BuildModel);

    private static IModel Model => _model.Value;

    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer("Server=localhost;Database=3dERP_ModelConstraintTests;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        using var context = new ErpDbContext(options);
        return context.Model;
    }

    private static IEntityType EntityType(Type clrType)
    {
        var entityType = Model.FindEntityType(clrType);
        Assert.NotNull(entityType);
        return entityType!;
    }

    private static IProperty Property(Type clrType, string name)
    {
        var property = EntityType(clrType).FindProperty(name);
        Assert.NotNull(property);
        return property!;
    }

    private static IIndex Index(Type clrType, string databaseName)
    {
        var index = EntityType(clrType).GetIndexes().SingleOrDefault(candidate =>
            candidate.GetDatabaseName() == databaseName);
        Assert.NotNull(index);
        return index!;
    }

    private static IForeignKey ForeignKey(Type dependentType, Type principalType, string propertyName)
    {
        var foreignKey = EntityType(dependentType).GetForeignKeys().SingleOrDefault(candidate =>
            candidate.PrincipalEntityType.ClrType == principalType
            && candidate.Properties.Count == 1
            && candidate.Properties[0].Name == propertyName);
        Assert.NotNull(foreignKey);
        return foreignKey!;
    }

    private static void AssertDecimal194(IProperty property)
    {
        Assert.Equal(19, property.GetPrecision());
        Assert.Equal(4, property.GetScale());
        Assert.Equal("decimal(19,4)", property.GetColumnType());
    }

    [Fact]
    public void InventoryBalance_UsesDecimal194AndRowVersionConcurrency()
    {
        AssertDecimal194(Property(typeof(InventoryBalance), nameof(InventoryBalance.OnHandQuantity)));

        var rowVersion = Property(typeof(InventoryBalance), nameof(InventoryBalance.RowVersion));
        Assert.True(rowVersion.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, rowVersion.ValueGenerated);
        Assert.Equal("rowversion", rowVersion.GetColumnType());
    }

    [Fact]
    public void InventoryBalance_WarehousePlusMaterial_IsUniqueIndexed()
    {
        var index = Index(
            typeof(InventoryBalance),
            "IX_InventoryBalances_WarehouseId_MaterialId");

        Assert.True(index.IsUnique);
        Assert.Collection(index.Properties,
            property => Assert.Equal(nameof(InventoryBalance.WarehouseId), property.Name),
            property => Assert.Equal(nameof(InventoryBalance.MaterialId), property.Name));
    }

    [Fact]
    public void InventoryBalance_ReferencesMasterDataWithRestrictDelete()
    {
        Assert.Equal("InventoryBalances", EntityType(typeof(InventoryBalance)).GetTableName());

        var warehouseFk = ForeignKey(typeof(InventoryBalance), typeof(Warehouse), nameof(InventoryBalance.WarehouseId));
        Assert.Equal(DeleteBehavior.Restrict, warehouseFk.DeleteBehavior);
        Assert.True(warehouseFk.IsRequired);

        var materialFk = ForeignKey(typeof(InventoryBalance), typeof(Material), nameof(InventoryBalance.MaterialId));
        Assert.Equal(DeleteBehavior.Restrict, materialFk.DeleteBehavior);
        Assert.True(materialFk.IsRequired);
    }

    [Fact]
    public void InventoryMovement_UsesDecimal194AndRequiredDocumentNo()
    {
        AssertDecimal194(Property(typeof(InventoryMovement), nameof(InventoryMovement.Quantity)));

        var documentNo = Property(typeof(InventoryMovement), nameof(InventoryMovement.DocumentNo));
        Assert.False(documentNo.IsNullable);
        Assert.Equal(64, documentNo.GetMaxLength());

        var remark = Property(typeof(InventoryMovement), nameof(InventoryMovement.Remark));
        Assert.True(remark.IsNullable);
        Assert.Equal(512, remark.GetMaxLength());

        var movementType = Property(typeof(InventoryMovement), nameof(InventoryMovement.MovementType));
        Assert.Equal("int", movementType.GetColumnType());
    }

    [Fact]
    public void InventoryMovement_MovementTypePlusDocumentNo_IsUniqueIndexed()
    {
        var index = Index(
            typeof(InventoryMovement),
            "IX_InventoryMovements_MovementType_DocumentNo");

        Assert.True(index.IsUnique);
        Assert.Collection(index.Properties,
            property => Assert.Equal(nameof(InventoryMovement.MovementType), property.Name),
            property => Assert.Equal(nameof(InventoryMovement.DocumentNo), property.Name));
    }

    [Fact]
    public void InventoryMovement_HasWarehouseMaterialTimeQueryIndex()
    {
        var index = Index(
            typeof(InventoryMovement),
            "IX_InventoryMovements_WarehouseId_MaterialId_OccurredAtUtc");

        Assert.False(index.IsUnique);
        Assert.Collection(index.Properties,
            property => Assert.Equal(nameof(InventoryMovement.WarehouseId), property.Name),
            property => Assert.Equal(nameof(InventoryMovement.MaterialId), property.Name),
            property => Assert.Equal(nameof(InventoryMovement.OccurredAtUtc), property.Name));
    }

    [Fact]
    public void InventoryMovement_ReferencesMasterDataWithOptionalPartners()
    {
        Assert.Equal("InventoryMovements", EntityType(typeof(InventoryMovement)).GetTableName());

        Assert.Equal(
            DeleteBehavior.Restrict,
            ForeignKey(typeof(InventoryMovement), typeof(Warehouse), nameof(InventoryMovement.WarehouseId)).DeleteBehavior);
        Assert.Equal(
            DeleteBehavior.Restrict,
            ForeignKey(typeof(InventoryMovement), typeof(Material), nameof(InventoryMovement.MaterialId)).DeleteBehavior);

        var supplierFk = ForeignKey(typeof(InventoryMovement), typeof(Supplier), nameof(InventoryMovement.SupplierId));
        Assert.Equal(DeleteBehavior.Restrict, supplierFk.DeleteBehavior);
        Assert.False(supplierFk.IsRequired);

        var customerFk = ForeignKey(typeof(InventoryMovement), typeof(Customer), nameof(InventoryMovement.CustomerId));
        Assert.Equal(DeleteBehavior.Restrict, customerFk.DeleteBehavior);
        Assert.False(customerFk.IsRequired);
    }

    [Fact]
    public void InventoryAlertRule_UsesDecimal194AndWarehousePlusMaterialUniqueIndex()
    {
        AssertDecimal194(Property(typeof(InventoryAlertRule), nameof(InventoryAlertRule.MinimumQuantity)));

        Assert.Equal("InventoryAlertRules", EntityType(typeof(InventoryAlertRule)).GetTableName());

        var index = Index(
            typeof(InventoryAlertRule),
            "IX_InventoryAlertRules_WarehouseId_MaterialId");

        Assert.True(index.IsUnique);
        Assert.Collection(index.Properties,
            property => Assert.Equal(nameof(InventoryAlertRule.WarehouseId), property.Name),
            property => Assert.Equal(nameof(InventoryAlertRule.MaterialId), property.Name));

        Assert.Equal(
            DeleteBehavior.Restrict,
            ForeignKey(typeof(InventoryAlertRule), typeof(Warehouse), nameof(InventoryAlertRule.WarehouseId)).DeleteBehavior);
        Assert.Equal(
            DeleteBehavior.Restrict,
            ForeignKey(typeof(InventoryAlertRule), typeof(Material), nameof(InventoryAlertRule.MaterialId)).DeleteBehavior);
    }
}
