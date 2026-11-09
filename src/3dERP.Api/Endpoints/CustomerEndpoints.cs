using ThreeDErp.Application.Customers;

namespace ThreeDErp.Api.Endpoints;

/// <summary>
/// /api/v1/customers 端点组。endpoint 只做参数接收与结果映射，
/// 业务逻辑全部在 CustomerService 中。
/// </summary>
public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/customers")
            .WithTags("Customers");

        group.MapGet(string.Empty, async (
                CustomerService customers,
                int? page,
                int? pageSize,
                string? keyword,
                bool? includeInactive,
                CancellationToken cancellationToken) =>
            {
                var result = await customers.SearchAsync(
                    page ?? 1,
                    pageSize ?? 10,
                    keyword,
                    includeInactive ?? false,
                    cancellationToken);

                return Results.Ok(result);
            })
            .WithName("ListCustomers")
            .WithSummary("分页查询客户，支持关键字搜索");

        group.MapGet("{id:guid}", async (
                Guid id,
                CustomerService customers,
                CancellationToken cancellationToken) =>
            {
                var customer = await customers.GetAsync(id, cancellationToken);
                return customer is null ? Results.NotFound() : Results.Ok(customer);
            })
            .WithName("GetCustomerById")
            .WithSummary("按 Id 获取客户");

        group.MapPost(string.Empty, async (
                CreateCustomerRequest request,
                CustomerService customers,
                CancellationToken cancellationToken) =>
            {
                var customer = await customers.CreateAsync(request, cancellationToken);
                return Results.CreatedAtRoute("GetCustomerById", new { id = customer.Id }, customer);
            })
            .WithName("CreateCustomer")
            .WithSummary("创建客户");

        group.MapPut("{id:guid}", async (
                Guid id,
                UpdateCustomerRequest request,
                CustomerService customers,
                CancellationToken cancellationToken) =>
            {
                var customer = await customers.UpdateAsync(id, request, cancellationToken);
                return Results.Ok(customer);
            })
            .WithName("UpdateCustomer")
            .WithSummary("更新客户");

        group.MapDelete("{id:guid}", async (
                Guid id,
                CustomerService customers,
                CancellationToken cancellationToken) =>
            {
                var customer = await customers.DeactivateAsync(id, cancellationToken);
                return Results.Ok(customer);
            })
            .WithName("DeactivateCustomer")
            .WithSummary("停用客户（软删除，不做物理删除）");

        return app;
    }
}
