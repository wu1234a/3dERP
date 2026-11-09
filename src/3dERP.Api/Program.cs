using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using ThreeDErp.Api.Endpoints;
using ThreeDErp.Api.Middleware;
using ThreeDErp.Application.Customers;
using ThreeDErp.Application.Materials;
using ThreeDErp.Infrastructure;
using ThreeDErp.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<CustomerRequestValidator>();
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<MaterialRequestValidator>();
builder.Services.AddScoped<MaterialService>();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ErpDbContext>("database");

builder.Services.AddCors(options =>
{
    options.AddPolicy("web", policy =>
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors("web");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/", () => Results.Ok(new
{
    name = "3dERP API",
    status = "ok",
    docs = "/scalar/v1"
}));

app.MapGet("/api/v1/meta", () => Results.Ok(new
{
    name = "3dERP",
    version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
    environment = app.Environment.EnvironmentName,
    utcNow = DateTime.UtcNow
}));

app.MapCustomerEndpoints();
app.MapMaterialEndpoints();

app.MapHealthChecks("/health");

app.Run();

/// <summary>
/// 供 WebApplicationFactory 集成测试引用（minimal hosting 的入口类）。
/// </summary>
public partial class Program;
