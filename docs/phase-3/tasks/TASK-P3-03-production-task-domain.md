# TASK-P3-03 · 生产任务领域模型与持久化

| 字段 | 值 |
| --- | --- |
| 里程碑 | M3.2 生产任务后端 |
| 依赖 | TASK-P3-01 |
| 优先级 | 高 |
| 状态 | 待开发 |

## 1. 背景与目标

建立生产任务域模型：3D 打印任务、排产时间字段、材料耗用记录与完工产出。
与销售订单行（可选）关联，支撑"接单生产"闭环。

## 2. 范围

### 做

- 领域实体：`ProductionTask`、`ProductionConsumption`、`ProductionTaskStatus`。
- EF Core 配置与迁移 `AddProductionTasks`。

### 不做

- 应用服务、API、耗用联动库存（TASK-P3-04 / P3-05）。
- 排产算法、设备台账、后处理工单拆分（本期仅时间字段与状态）。

## 3. 交付物

```text
src/3dERP.Domain/Production/ProductionTask.cs
src/3dERP.Domain/Production/ProductionConsumption.cs
src/3dERP.Domain/Production/ProductionTaskStatus.cs
src/3dERP.Infrastructure/Persistence/Configurations/ProductionTaskConfiguration.cs
src/3dERP.Infrastructure/Persistence/Configurations/ProductionConsumptionConfiguration.cs
src/3dERP.Infrastructure/Persistence/ErpDbContext.cs        （注册 DbSet）
src/3dERP.Infrastructure/Migrations/*_AddProductionTasks.cs
```

## 4. 模型定义

### ProductionTask

| 字段 | 类型 | 规则 |
| --- | --- | --- |
| Id | Guid | `Guid.CreateVersion7()` |
| DocumentNo | string | 必填，2-64 位，表内唯一，建议 `PT-YYYY-NNNN` |
| Status | ProductionTaskStatus | 默认 `Pending` |
| SalesOrderLineId | Guid? | 可空；关联 `SalesOrderLines`（Restrict），支持独立任务 |
| OutputMaterialId | Guid | 外键 `Materials`（产出成品物料） |
| OutputWarehouseId | Guid | 外键 `Warehouses`（完工入库仓库） |
| PlannedQuantity | decimal(19,4) | > 0 |
| CompletedQuantity | decimal(19,4) | ≥ 0，默认 0，完工登记（P3-04）写入 |
| ScheduledStartUtc | DateTime? | 排产开始，仅记录 |
| ScheduledEndUtc | DateTime? | 排产结束，仅记录 |
| Remark | string? | ≤ 512 |
| CreatedAtUtc / UpdatedAtUtc | DateTime | UTC |
| RowVersion | byte[] | rowversion |
| Consumptions | List\<ProductionConsumption\> | 任务耗用明细 |

### ProductionConsumption

| 字段 | 类型 | 规则 |
| --- | --- | --- |
| Id | Guid | `Guid.CreateVersion7()` |
| TaskId | Guid | 外键 `ProductionTasks`，随任务级联 |
| Sequence | int | 任务内序号 ≥ 1，`(TaskId, Sequence)` 唯一 |
| MaterialId | Guid | 外键 `Materials`（被耗用物料） |
| Quantity | decimal(19,4) | > 0 |
| InventoryMovementId | Guid | 关联库存 Out 流水，便于追溯 |
| RecordedAtUtc | DateTime | UTC |
| Remark | string? | ≤ 256 |

### ProductionTaskStatus

```csharp
Pending = 1, Printing = 2, PostProcessing = 3, Completed = 4, Cancelled = 5
```

流转规则（服务层校验，P3-04 落地）：

| 当前 | 目标 | 条件 |
| --- | --- | --- |
| Pending | Printing | 开始打印 |
| Printing | PostProcessing | 转后处理 |
| Printing / PostProcessing | Completed | 完工登记（写成品入库） |
| Pending / Printing / PostProcessing | Cancelled | 取消；`Completed`、`Cancelled` 为终态 |

材料耗用与完工登记仅允许 `Printing` / `PostProcessing` 状态。

## 5. 业务规则

- 单据不物理删除，只有取消；实体 `sealed` + 中文 XML 注释，风格对齐阶段 1/2。
- 时间 UTC、数量 `decimal(19,4)`、主键 `Guid.CreateVersion7()`、行版本并发令牌。

## 6. 验收标准

1. 迁移 `AddProductionTasks` 生成并可直接应用；唯一索引 / 外键 / 精度与本文一致。
2. `dotnet build 3dERP.slnx` 通过。

## 7. 验证命令

```powershell
dotnet build 3dERP.slnx
```
