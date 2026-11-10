using Microsoft.EntityFrameworkCore;
using ThreeDErp.Domain.Customers;
using ThreeDErp.Domain.Materials;
using ThreeDErp.Domain.Suppliers;
using ThreeDErp.Domain.Warehouses;

namespace ThreeDErp.Infrastructure.Persistence;

/// <summary>
/// Root database context. Domain modules add their own entity configurations and
/// are discovered from this assembly, so the context stays small as modules grow.
/// </summary>
public sealed class ErpDbContext(DbContextOptions<ErpDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Material> Materials => Set<Material>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ErpDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
