using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using ThreeDErp.Application.Common;
using ThreeDErp.Application.Inventory;
using ThreeDErp.Domain.Inventory;
using ThreeDErp.Infrastructure.Persistence;

namespace ThreeDErp.Infrastructure.Persistence.Repositories;

/// <summary>
/// 库存仓储的 EF Core 实现。余额查询 AsNoTracking 并 Include 仓库 / 物料主数据，
/// 按 WarehouseId / MaterialId / 物料编码 / 名称过滤，稳定排序保证分页结果确定。
/// 入库 / 出库事务在同一数据库事务内写流水并更新余额（非关系型 provider 退化为
/// 单次 SaveChanges），唯一索引与 rowversion 并发冲突统一翻译为 ConflictException。
/// </summary>
public sealed class InventoryRepository(ErpDbContext context) : IInventoryRepository
{
    /// <summary>MovementType + DocumentNo 唯一索引名，用于区分单据号冲突。</summary>
    private const string MovementDocumentNoIndexName = "IX_InventoryMovements_MovementType_DocumentNo";

    public async Task<PagedResult<InventoryBalance>> FindBalancesAsync(
        int page,
        int pageSize,
        Guid? warehouseId,
        Guid? materialId,
        string? keyword,
        CancellationToken cancellationToken = default)
    {
        IQueryable<InventoryBalance> query = context.InventoryBalances
            .AsNoTracking()
            .Include(balance => balance.Warehouse)
            .Include(balance => balance.Material);

        if (warehouseId is not null)
        {
            query = query.Where(balance => balance.WarehouseId == warehouseId);
        }

        if (materialId is not null)
        {
            query = query.Where(balance => balance.MaterialId == materialId);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(balance =>
                balance.Material.Code.Contains(keyword)
                || balance.Material.Name.Contains(keyword));
        }

        // WarehouseId + MaterialId 唯一，编码排序保证稳定分页。
        query = query
            .OrderBy(balance => balance.Warehouse.Code)
            .ThenBy(balance => balance.Material.Code);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<InventoryBalance>(items, page, pageSize, totalCount);
    }

    public Task<InventoryBalance?> FindBalanceAsync(
        Guid warehouseId,
        Guid materialId,
        CancellationToken cancellationToken = default) =>
        context.InventoryBalances.FirstOrDefaultAsync(
            balance => balance.WarehouseId == warehouseId && balance.MaterialId == materialId,
            cancellationToken);

    public Task<bool> DocumentNoExistsAsync(
        InventoryMovementType movementType,
        string documentNo,
        CancellationToken cancellationToken = default) =>
        context.InventoryMovements
            .AsNoTracking()
            .AnyAsync(movement => movement.MovementType == movementType
                && movement.DocumentNo == documentNo,
                cancellationToken);

    public Task<bool> WarehouseExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Warehouses
            .AsNoTracking()
            .AnyAsync(warehouse => warehouse.Id == id && warehouse.IsActive, cancellationToken);

    public Task<bool> MaterialExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Materials
            .AsNoTracking()
            .AnyAsync(material => material.Id == id && material.IsActive, cancellationToken);

    public Task<bool> SupplierExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Suppliers
            .AsNoTracking()
            .AnyAsync(supplier => supplier.Id == id && supplier.IsActive, cancellationToken);

    public Task<bool> CustomerExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Customers
            .AsNoTracking()
            .AnyAsync(customer => customer.Id == id && customer.IsActive, cancellationToken);

    public async Task<InventoryBalance> RecordMovementAsync(
        InventoryMovement movement,
        InventoryBalance? balance,
        CancellationToken cancellationToken = default)
    {
        // 首次入库且尚无余额时在同一事务内创建（出库不足的情况已在服务层拦截）。
        var creatingBalance = balance is null;
        var targetBalance = balance ?? new InventoryBalance
        {
            Id = Guid.CreateVersion7(),
            WarehouseId = movement.WarehouseId,
            MaterialId = movement.MaterialId,
            OnHandQuantity = movement.Quantity
        };

        if (!context.Database.IsRelational())
        {
            // InMemory 等 provider 不支持显式事务：单次 SaveChanges 本身是原子的。
            await PersistAsync(targetBalance, movement, creatingBalance, cancellationToken);
            return targetBalance;
        }

        // EnableRetryOnFailure 要求显式事务在执行策略内运行，整个事务作为一个可重试单元；
        // 每次尝试重新设置实体状态，保证写入被完整重新执行。
        var write = new MovementWrite(targetBalance, movement, creatingBalance);
        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(
            write,
            (_, attemptCancellationToken) =>
                ExecuteInTransactionAsync(write, attemptCancellationToken),
            cancellationToken);

        return targetBalance;
    }

    private async Task ExecuteInTransactionAsync(MovementWrite write, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await PersistAsync(write.Balance, write.Movement, write.CreatingBalance, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task PersistAsync(
        InventoryBalance balance,
        InventoryMovement movement,
        bool creatingBalance,
        CancellationToken cancellationToken)
    {
        // 显式设置状态（而非依赖变更追踪），使瞬态失败后的重试能完整重放写入；
        // 已提交但响应失败的场景由 rowversion 并发令牌兜底（翻译为 409）。
        context.Entry(balance).State = creatingBalance ? EntityState.Added : EntityState.Modified;
        context.Entry(movement).State = EntityState.Added;

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // rowversion 不匹配：余额已被其他事务修改，本次记账作废。
            throw new ConflictException("库存余额已被其他操作修改，请刷新后重试。");
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            // 并发窗口内同一单据号 / 同一仓库物料余额被其他事务抢先写入。
            throw new ConflictException(
                exception.Message.Contains(MovementDocumentNoIndexName)
                    ? $"单据号 {movement.DocumentNo} 已存在，请勿重复记账。"
                    : "库存余额记录冲突，请重试。");
        }
        catch (DbUpdateException exception) when (IsForeignKeyViolation(exception))
        {
            // 主数据在校验之后、保存之前被删除（外键限制删除）的并发窗口。
            throw new ConflictException("关联的仓库、物料、供应商或客户不存在或已被删除。");
        }
    }

    /// <summary>单次事务写入所需的实体集合（执行策略按状态传递，便于重试）。</summary>
    private sealed record MovementWrite(
        InventoryBalance Balance,
        InventoryMovement Movement,
        bool CreatingBalance);

    /// <summary>SQL Server 唯一索引 / 主键冲突错误号。</summary>
    private static bool IsDuplicateKey(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException
            && sqlException.Number is 2601 or 2627;

    /// <summary>SQL Server 外键冲突错误号。</summary>
    private static bool IsForeignKeyViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException
            && sqlException.Number == 547;
}
