using Microsoft.EntityFrameworkCore;

namespace ThreeDErp.Infrastructure.Persistence;

/// <summary>
/// Root database context. Domain modules add their own entity configurations and
/// are discovered from this assembly, so the context stays small as modules grow.
/// </summary>
public sealed class ErpDbContext(DbContextOptions<ErpDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ErpDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
