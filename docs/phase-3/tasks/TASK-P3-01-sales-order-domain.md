# TASK-P3-01 · 销售订单领域模型与持久化

| 字段 | 值 |
| --- | --- |
| 里程碑 | M3.1 销售订单后端 |
| 依赖 | 无 |
| 优先级 | 高 |
| 状态 | 待开发 |

## 1. 背景与目标

阶段 1/2 只包含主数据与库存。本任务建立第一组业务单据的领域模型与持久化：
销售订单（含行），为 TASK-P3-02 的服务与 API 打底。

## 2. 范围

### 做

- 领域实体：`SalesOrder`、`SalesOrderLine`、`SalesOrderStatus`。
- EF Core 配置与迁移 `AddSalesOrders`。
- 行上预留 `ShippedQuantity`（默认 0），供 M3.3 发货使用，避免二次迁移。

### 不做

- 应用服务、API 端点、前端页面（TASK-P3-02 / P3-07）。
- 发货单实体（TASK-P3-06 / M3.3 迁移）。

## 3. 交付物

```text
src/3dERP.Domain/Sales/SalesOrder.cs
src/3dERP.Domain/Sales/SalesOrderLine.cs
src/3dERP.Domain/Sales/SalesOrderStatus.cs
src/3dERP.Infrastructure/Persistence/Configurations/SalesOrderConfiguration.cs
src/3dERP.Infrastructure/Persistence/Configurations/SalesOrderLineConfiguration.cs
src/3dERP.Infrastructure/Persistence/ErpDbContext.cs        （注册 DbSet）
src/3dERP.Infrastructure/Migrations/*_AddSalesOrders.cs     （dotnet ef migrations add 生成）
```

命名空间沿用 `ThreeDErp.Domain.Sales` / `ThreeDErp.Infrastructure.*`（项目目录为 `3dERP.*`）。

## 4. 模型定义

### SalesOrder

| 字段 | 类型 | 规则 |
| --- | --- | --- |
| Id | Guid | `Guid.CreateVersion7()` |
| DocumentNo | string | 必填，2-64 位，表内唯一索引，建议格式 `SO-YYYY-NNNN` |
| CustomerId | Guid | 外键 `Customers`， Restrict |
| Status | SalesOrderStatus | 默认 `Draft`，int 存储 |
| OrderDateUtc | DateTime | 必填，UTC |
| ExpectedDeliveryDateUtc | DateTime? | 可空 |
| Remark | string? | ≤ 512 |
| CreatedAtUtc / UpdatedAtUtc | DateTime | UTC |
| RowVersion | byte[] | rowversion 乐观并发 |
| Lines | List\<SalesOrderLine\> | 级联保存，行随单据头变更 |

### SalesOrderLine

| 字段 | 类型 | 规则 |
| --- | --- | --- |
| Id | Guid | `Guid.CreateVersion7()` |
| OrderId | Guid | 外键 `SalesOrders`，随单据级联 |
| LineNo | int | ≥ 1，`(OrderId, LineNo)` 唯一 |
| MaterialId | Guid | 外键 `Materials`，Restrict |
| Quantity | decimal(19,4) | > 0 |
| UnitPrice | decimal(19,4) | ≥ 0 |
| ShippedQuantity | decimal(19,4) | ≥ 0，默认 0，仅发货登记（P3-06）更新 |
| Remark | string? | ≤ 256 |
| RowVersion | byte[] | rowversion |

### SalesOrderStatus

```csharp
Draft = 1, Confirmed = 2, InProduction = 3, Completed = 4, Cancelled = 5
```

流转规则（服务层校验，本任务只落枚举与注释）：

| 当前 | 目标 | 条件 |
| --- | --- | --- |
| Draft | Confirmed | 至少一行且行合法、客户存在并启用 |
| Confirmed / InProduction | InProduction | 创建关联生产任务时自动流转（幂等，P3-04） |
| Confirmed / InProduction | Completed | 全部行 `ShippedQuantity == Quantity`（P3-06 自动流转） |
| Draft / Confirmed / InProduction | Cancelled | 无已发货数量且无未完成任务；`Completed`、`Cancelled` 为终态 |

## 5. 业务规则

- 业务单据不做物理删除：不生成订单的 DELETE；状态用枚举注释写清终态。
- 时间一律 UTC；金额数量一律 `decimal`。
- 实体为 `sealed class`，属性带中文 XML 注释，风格与 `Customer` / `InventoryMovement` 一致。

## 6. 验收标准

1. `dotnet ef migrations add AddSalesOrders --project src/3dERP.Infrastructure` 生成迁移，
   快照只包含本次变更；唯一索引、外键行为、decimal 精度与注释一致。
2. `dotnet build 3dERP.slnx` 通过。
3. 迁移可应用到 `3dERP_Dev`（本地验证）。

## 7. 验证命令

```powershell
dotnet build 3dERP.slnx
```
