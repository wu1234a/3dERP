using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ThreeDErp.Application.Common;
using ThreeDErp.Application.Suppliers;
using ThreeDErp.Domain.Suppliers;
using ThreeDErp.Infrastructure.Persistence;

namespace ThreeDErp.Infrastructure.Persistence.Repositories;

/// <summary>
/// 供应商仓储的 EF Core 实现。唯一索引冲突与并发冲突统一翻译为
/// Application 层的 ConflictException。
/// </summary>
public sealed class SupplierRepository(ErpDbContext context) : ISupplierRepository
{
    public async Task<PagedResult<Supplier>> FindAsync(
        int page,
        int pageSize,
        string? keyword,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var query = context.Suppliers.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(supplier => supplier.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(supplier =>
                supplier.Code.Contains(keyword)
                || supplier.Name.Contains(keyword)
                || (supplier.ContactName != null && supplier.ContactName.Contains(keyword))
                || (supplier.Phone != null && supplier.Phone.Contains(keyword))
                || (supplier.Email != null && supplier.Email.Contains(keyword)));
        }

        query = query
            .OrderBy(supplier => supplier.Code)
            .ThenByDescending(supplier => supplier.CreatedAtUtc);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Supplier>(items, page, pageSize, totalCount);
    }

    public Task<Supplier?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Suppliers.FirstOrDefaultAsync(supplier => supplier.Id == id, cancellationToken);

    public Task<bool> CodeExistsAsync(
        string code,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default) =>
        context.Suppliers
            .AsNoTracking()
            .AnyAsync(supplier => supplier.Code == code
                && (excludeId == null || supplier.Id != excludeId),
                cancellationToken);

    public async Task AddAsync(Supplier supplier, CancellationToken cancellationToken = default)
    {
        context.Suppliers.Add(supplier);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            throw new ConflictException($"供应商编码 {supplier.Code} 已存在。");
        }
    }

    public async Task UpdateAsync(Supplier supplier, CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("供应商已被其他用户修改，请刷新后重试。");
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            throw new ConflictException($"供应商编码 {supplier.Code} 已存在。");
        }
    }

    /// <summary>SQL Server 唯一索引 / 主键冲突错误号。</summary>
    private static bool IsDuplicateKey(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException
            && sqlException.Number is 2601 or 2627;
}
