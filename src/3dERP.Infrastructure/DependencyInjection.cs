using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ThreeDErp.Application.Customers;
using ThreeDErp.Application.Inventory;
using ThreeDErp.Application.Materials;
using ThreeDErp.Application.Suppliers;
using ThreeDErp.Application.Warehouses;
using ThreeDErp.Infrastructure.Persistence;
using ThreeDErp.Infrastructure.Persistence.Repositories;

namespace ThreeDErp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

        services.AddDbContext<ErpDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(ErpDbContext).Assembly.FullName);
                sql.EnableRetryOnFailure(3);
            }));

        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IMaterialRepository, MaterialRepository>();
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<IInventoryAlertRepository, InventoryAlertRepository>();
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<IWarehouseRepository, WarehouseRepository>();

        return services;
    }
}
