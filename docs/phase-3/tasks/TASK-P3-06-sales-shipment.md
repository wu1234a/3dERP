# TASK-P3-06 · 销售发货与订单闭环

| 字段 | 值 |
| --- | --- |
| 里程碑 | M3.3 发货与订单闭环 |
| 依赖 | TASK-P3-02 |
| 优先级 | 高 |
| 状态 | 待开发 |

## 1. 背景与目标

销售发货把订单推进到完成：按行登记发货数量，写入销售出库流水（带客户），
全部行发足后订单自动 `Completed`。支持部分发货与分次发货，禁止超发。

## 2. 范围

### 做

- 发货单实体：`SalesShipment` + `SalesShipmentLine`，迁移 `AddSalesShipments`。
- `POST /api/v1/sales-orders/{id}/ship`：一次可发多行，事务内写发货单、行、
  `ShippedQuantity` 与库存 Out 流水。
- `GET /api/v1/sales-orders/{id}/shipments`：发货历史。
- 单元测试。

### 不做

- 物流 / 运单信息、签收确认；应收与发票（财务模块）。

## 3. 交付物

```text
src/3dERP.Domain/Sales/SalesShipment.cs
src/3dERP.Domain/Sales/SalesShipmentLine.cs
src/3dERP.Infrastructure/Persistence/Configurations/SalesShipmentConfiguration.cs
src/3dERP.Infrastructure/Persistence/Configurations/SalesShipmentLineConfiguration.cs
src/3dERP.Infrastructure/Migrations/*_AddSalesShipments.cs
src/3dERP.Application/Sales/SalesShipmentDto.cs
src/3dERP.Application/Sales/ShipSalesOrderRequest.cs
src/3dERP.Application/Sales/ShipSalesOrderRequestValidator.cs
src/3dERP.Application/Sales/ISalesOrderRepository.cs              （扩展发货查询）
src/3dERP.Application/Sales/SalesShipmentService.cs
src/3dERP.Application/Sales/SalesOrderService.cs                  （订单状态推进）
src/3dERP.Infrastructure/Persistence/Repositories/SalesOrderRepository.cs
src/3dERP.Api/Endpoints/SalesOrderEndpoints.cs
tests/3dERP.UnitTests/Sales/SalesShipmentServiceTests.cs
```

## 4. 模型定义

### SalesShipment

| 字段 | 类型 | 规则 |
| --- | --- | --- |
| Id | Guid | `Guid.CreateVersion7()` |
| OrderId | Guid | 外键 `SalesOrders`（Restrict） |
| ShipmentNo | int | 订单内序号 ≥ 1，`(OrderId, ShipmentNo)` 唯一 |
| ShippedAtUtc | DateTime | UTC |
| Remark | string? | ≤ 512 |
| CreatedAtUtc | DateTime | UTC |
| RowVersion | byte[] | rowversion |

### SalesShipmentLine

| 字段 | 类型 | 规则 |
| --- | --- | --- |
| Id | Guid | `Guid.CreateVersion7()` |
| ShipmentId | Guid | 外键 `SalesShipments`，随发货单级联 |
| OrderLineId | Guid | 外键 `SalesOrderLines` |
| WarehouseId | Guid | 外键 `Warehouses`（出库仓库） |
| Quantity | decimal(19,4) | > 0 |
| InventoryMovementId | Guid | 关联库存 Out 流水 |

## 5. API 与业务规则

- 请求体：`{ "lines": [ { "salesOrderLineId": "...", "quantity": 5, "warehouseId": "..." } ], "note": "..." }`。
- 允许发货的订单状态：`Confirmed` / `InProduction`；`Draft` / `Completed` / `Cancelled` → 409。
- 行校验：`salesOrderLineId` 必须属于该订单（404）；`quantity` > 0；
  `ShippedQuantity + quantity ≤ line.Quantity`，否则 409（"超出订单行可发数量。"）；
  同一请求内不允许重复的订单行。
- 出库流水：`MovementType = Out`、`CustomerId = 订单客户`、
  `DocumentNo = "{订单单号}-S-{ShipmentNo:00}"`（每次发货一个流水，多行数量合并）；
  物料取订单行 `MaterialId`，仓库取请求指定仓库。
- 原子性：发货单、行、`ShippedQuantity` 更新、流水写入在同一数据库事务；任一行
  库存不足或校验失败则整单回滚，错误沿用库存服务文案（409）。
- 完成判定：发货成功后若全部行 `ShippedQuantity == Quantity`，订单状态置为
  `Completed`（不覆盖 `Cancelled`，取消校验已在发货前完成）。
- 发货后订单禁止取消（P3-02 已有规则）；发货单不可修改、不可删除（流水不可变）。

## 6. 验收标准

1. 部分发货后订单仍为 `InProduction`，行 `ShippedQuantity` 正确累计；发足后订单 `Completed`。
2. 超发、库存不足、重复订单行、非法状态均返回 409 且不留任何脏数据（整单回滚）。
3. 流水 `DocumentNo` 序号按订单递增（`SO-2026-0001-S-01`、`-S-02`…）。
4. 单元测试覆盖上述全部分支；`dotnet build 3dERP.slnx`、`dotnet test 3dERP.slnx` 通过。

## 7. 验证命令

```powershell
dotnet build 3dERP.slnx
dotnet test 3dERP.slnx
```
