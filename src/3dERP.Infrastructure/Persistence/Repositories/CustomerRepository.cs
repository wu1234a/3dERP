using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ThreeDErp.Application.Common;
using ThreeDErp.Application.Customers;
using ThreeDErp.Domain.Customers;
using ThreeDErp.Infrastructure.Persistence;

namespace ThreeDErp.Infrastructure.Persistence.Repositories;

/// <summary>
/// 客户仓储的 EF Core 实现。唯一索引冲突与并发冲突统一翻译为
/// Application 层的 ConflictException。
/// </summary>
public sealed class CustomerRepository(ErpDbContext context) : ICustomerRepository
{
    public async Task<PagedResult<Customer>> FindAsync(
        int page,
        int pageSize,
        string? keyword,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var query = context.Customers.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(customer => customer.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(customer =>
                customer.Code.Contains(keyword)
                || customer.Name.Contains(keyword)
                || (customer.ContactName != null && customer.ContactName.Contains(keyword))
                || (customer.Phone != null && customer.Phone.Contains(keyword))
                || (customer.Email != null && customer.Email.Contains(keyword)));
        }

        query = query
            .OrderBy(customer => customer.Code)
            .ThenByDescending(customer => customer.CreatedAtUtc);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Customer>(items, page, pageSize, totalCount);
    }

    public Task<Customer?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Customers.FirstOrDefaultAsync(customer => customer.Id == id, cancellationToken);

    public Task<bool> CodeExistsAsync(
        string code,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default) =>
        context.Customers
            .AsNoTracking()
            .AnyAsync(customer => customer.Code == code
                && (excludeId == null || customer.Id != excludeId),
                cancellationToken);

    public async Task AddAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        context.Customers.Add(customer);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            throw new ConflictException($"客户编码 {customer.Code} 已存在。");
        }
    }

    public async Task UpdateAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("客户已被其他用户修改，请刷新后重试。");
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            throw new ConflictException($"客户编码 {customer.Code} 已存在。");
        }
    }

    /// <summary>SQL Server 唯一索引 / 主键冲突错误号。</summary>
    private static bool IsDuplicateKey(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException
            && sqlException.Number is 2601 or 2627;
}
