using ThreeDErp.Application.Common;
using ThreeDErp.Domain.Inventory;

namespace ThreeDErp.Application.Inventory;

/// <summary>
/// 库存入库 / 出库事务服务。校验、主数据存在性、单据号防重与库存充足性等
/// 业务规则集中在这里；余额与流水的原子落库由仓储的事务实现完成。
/// </summary>
public sealed class InventoryTransactionService(
    IInventoryRepository repository,
    InventoryRequestValidator validator)
{
    /// <summary>
    /// 入库：无余额时新建，已有余额则累加现有量，并写入 In 类型流水。
    /// </summary>
    public Task<InventoryMovementDto> RecordInboundAsync(
        InventoryTransactionRequest request,
        CancellationToken cancellationToken = default) =>
        RecordAsync(InventoryMovementType.In, request, cancellationToken);

    /// <summary>
    /// 出库：现有量必须足够（禁止负库存），扣减后写入 Out 类型流水。
    /// </summary>
    public Task<InventoryMovementDto> RecordOutboundAsync(
        InventoryTransactionRequest request,
        CancellationToken cancellationToken = default) =>
        RecordAsync(InventoryMovementType.Out, request, cancellationToken);

    private async Task<InventoryMovementDto> RecordAsync(
        InventoryMovementType movementType,
        InventoryTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var errors = validator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var documentNo = InventoryRequestValidator.Normalize(request.DocumentNo);

        // 同一移动类型下防重；不同类型（先出后入等）允许复用单据号，与唯一索引一致。
        if (await repository.DocumentNoExistsAsync(movementType, documentNo, cancellationToken))
        {
            throw new ConflictException($"单据号 {documentNo} 已存在，请勿重复记账。");
        }

        await EnsureMasterDataExistsAsync(request, cancellationToken);

        var balance = await repository.FindBalanceAsync(
            request.WarehouseId,
            request.MaterialId,
            cancellationToken);

        // 出库禁止负库存：无余额或现有量不足时拒绝记账。
        if (movementType == InventoryMovementType.Out
            && (balance?.OnHandQuantity ?? 0m) < request.Quantity)
        {
            throw new ConflictException(
                $"库存不足：当前现有量 {(balance?.OnHandQuantity ?? 0m):0.####}，出库数量 {request.Quantity:0.####}。");
        }

        if (balance is not null)
        {
            balance.OnHandQuantity = movementType == InventoryMovementType.In
                ? balance.OnHandQuantity + request.Quantity
                : balance.OnHandQuantity - request.Quantity;
        }

        var movement = new InventoryMovement
        {
            Id = Guid.CreateVersion7(),
            MovementType = movementType,
            DocumentNo = documentNo,
            WarehouseId = request.WarehouseId,
            MaterialId = request.MaterialId,
            Quantity = request.Quantity,
            SupplierId = request.SupplierId,
            CustomerId = request.CustomerId,
            OccurredAtUtc = DateTime.UtcNow,
            Remark = Optional(request.Note)
        };

        // balance 为 null（首次入库）时由仓储在同一事务内创建余额。
        var resultingBalance = await repository.RecordMovementAsync(movement, balance, cancellationToken);
        return InventoryMovementDto.FromEntity(movement, resultingBalance);
    }

    /// <summary>
    /// 记账涉及的主数据必须存在且启用（软删除后不可再发生库存事务）。
    /// </summary>
    private async Task EnsureMasterDataExistsAsync(
        InventoryTransactionRequest request,
        CancellationToken cancellationToken)
    {
        if (!await repository.WarehouseExistsAsync(request.WarehouseId, cancellationToken))
        {
            throw new NotFoundException("仓库不存在或已停用。");
        }

        if (!await repository.MaterialExistsAsync(request.MaterialId, cancellationToken))
        {
            throw new NotFoundException("物料不存在或已停用。");
        }

        if (request.SupplierId is { } supplierId
            && !await repository.SupplierExistsAsync(supplierId, cancellationToken))
        {
            throw new NotFoundException("供应商不存在或已停用。");
        }

        if (request.CustomerId is { } customerId
            && !await repository.CustomerExistsAsync(customerId, cancellationToken))
        {
            throw new NotFoundException("客户不存在或已停用。");
        }
    }

    /// <summary>去除首尾空白；空白视为 null。</summary>
    private static string? Optional(string? value)
    {
        var normalized = InventoryRequestValidator.Normalize(value);
        return normalized.Length == 0 ? null : normalized;
    }
}
