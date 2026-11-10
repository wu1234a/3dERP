using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ThreeDErp.Infrastructure.Persistence;

namespace ThreeDErp.IntegrationTests.Warehouses;

/// <summary>
/// 集成测试宿主：复用真实 Program 管道（路由 / 模型绑定 / ProblemDetails / 异常映射），
/// 仅把 SQL Server 替换为 EF InMemory 数据库，测试不依赖外部数据库。
/// 数据库名与 InMemoryDatabaseRoot 在实例上固定（AddDbContext 的选项默认按 Scope 重建，
/// 名称 / 根必须固定才能让同一测试内的多次请求共享数据），每个工厂实例相互隔离。
/// </summary>
public sealed class WarehousesApiFactory : WebApplicationFactory<Program>
{
    private readonly InMemoryDatabaseRoot _databaseRoot = new();

    private string DatabaseName { get; } = $"warehouses-tests-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            // 移除 AddInfrastructure 注册的 SQL Server 选项配置，改用 InMemory。
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<ErpDbContext>));
            services.AddDbContext<ErpDbContext>(options =>
                options.UseInMemoryDatabase(DatabaseName, _databaseRoot));
        });
    }
}
