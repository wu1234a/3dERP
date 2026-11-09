using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ThreeDErp.Application.Common;
using ThreeDErp.Application.Materials;
using ThreeDErp.Domain.Materials;
using ThreeDErp.Infrastructure.Persistence;

namespace ThreeDErp.Infrastructure.Persistence.Repositories;

/// <summary>
/// 物料仓储的 EF Core 实现。关键字同时匹配编码 / 名称 / 物料类型，
/// 唯一索引冲突与并发冲突统一翻译为 Application 层的 ConflictException。
/// </summary>
public sealed class MaterialRepository(ErpDbContext context) : IMaterialRepository
{
    public async Task<PagedResult<Material>> FindAsync(
        int page,
        int pageSize,
        string? keyword,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var query = context.Materials.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(material => material.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(material =>
                material.Code.Contains(keyword)
                || material.Name.Contains(keyword)
                || material.MaterialType.Contains(keyword)
                || (material.Specification != null && material.Specification.Contains(keyword)));
        }

        query = query
            .OrderBy(material => material.Code)
            .ThenByDescending(material => material.CreatedAtUtc);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Material>(items, page, pageSize, totalCount);
    }

    public Task<Material?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Materials.FirstOrDefaultAsync(material => material.Id == id, cancellationToken);

    public Task<bool> CodeExistsAsync(
        string code,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default) =>
        context.Materials
            .AsNoTracking()
            .AnyAsync(material => material.Code == code
                && (excludeId == null || material.Id != excludeId),
                cancellationToken);

    public async Task AddAsync(Material material, CancellationToken cancellationToken = default)
    {
        context.Materials.Add(material);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            throw new ConflictException($"物料编码 {material.Code} 已存在。");
        }
    }

    public async Task UpdateAsync(Material material, CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("物料已被其他用户修改，请刷新后重试。");
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            throw new ConflictException($"物料编码 {material.Code} 已存在。");
        }
    }

    /// <summary>SQL Server 唯一索引 / 主键冲突错误号。</summary>
    private static bool IsDuplicateKey(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException
            && sqlException.Number is 2601 or 2627;
}
