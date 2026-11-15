# TASK-P3-04 · 生产任务应用服务与 API（含完工入库）

| 字段 | 值 |
| --- | --- |
| 里程碑 | M3.2 生产任务后端 |
| 依赖 | TASK-P3-02、TASK-P3-03 |
| 优先级 | 高 |
| 状态 | 待开发 |

## 1. 背景与目标

提供生产任务的用例层与端点：创建（可关联订单行）、开始、后处理、完工、取消、查询。
完工登记在任务内写入成品入库（库存 In 流水），并回写订单状态 `Confirmed → InProduction`。

## 2. 范围

### 做

- DTO、请求模型、校验器、`IProductionTaskRepository` / `ProductionTaskRepository`。
- `ProductionTaskService`：创建 / 状态流转 / 完工登记（成品入库）。
- `ProductionTaskEndpoints`（`/api/v1/production-tasks`）与 `Program.cs` 注册。
- 单元测试（fake repository）。

### 不做

- 材料耗用的库存联动（TASK-P3-05）；前端（TASK-P3-08）。

## 3. 交付物

```text
src/3dERP.Application/Production/ProductionTaskDto.cs
src/3dERP.Application/Production/ProductionTaskRequests.cs        （Create / Start / PostProcess / Complete / Cancel）
src/3dERP.Application/Production/ProductionTaskRequestValidator.cs
src/3dERP.Application/Production/IProductionTaskRepository.cs
src/3dERP.Application/Production/ProductionTaskService.cs
src/3dERP.Infrastructure/Persistence/Repositories/ProductionTaskRepository.cs
src/3dERP.Api/Endpoints/ProductionTaskEndpoints.cs
src/3dERP.Api/Program.cs                                          （注册 validator / service）
tests/3dERP.UnitTests/Production/ProductionTaskRequestValidatorTests.cs
tests/3dERP.UnitTests/Production/FakeProductionTaskRepository.cs
tests/3dERP.UnitTests/Production/ProductionTaskServiceTests.cs
```

## 4. API 设计

| 方法与路径 | 名称 | 说明 |
| --- | --- | --- |
| `GET /api/v1/production-tasks` | ListProductionTasks | 分页，支持 `status`、`salesOrderLineId`、`keyword` |
| `GET /api/v1/production-tasks/{id}` | GetProductionTaskById | 含耗用明细 |
| `POST /api/v1/production-tasks` | CreateProductionTask | 创建 `Pending` 任务 |
| `POST /api/v1/production-tasks/{id}/start` | StartProductionTask | `Pending → Printing` |
| `POST /api/v1/production-tasks/{id}/post-process` | PostProcessProductionTask | `Printing → PostProcessing` |
| `POST /api/v1/production-tasks/{id}/complete` | CompleteProductionTask | 完工登记 + 成品入库，任务 → `Completed` |
| `POST /api/v1/production-tasks/{id}/cancel` | CancelProductionTask | 非终态可取消 |

完工请求体：`{ "quantity": 12.5, "note": "..." }`。

## 5. 业务规则

### 校验器（400）

- `DocumentNo` 2-64 位（同任务文档规则）；`PlannedQuantity` > 0；
  `OutputMaterialId` / `OutputWarehouseId` 必填；完工 `quantity` > 0；
  `ScheduledEndUtc`（如填写）不得早于 `ScheduledStartUtc`。

### 服务层（404 / 409）

- 创建：产出物料 / 完工仓库不存在或停用 → 404；单据号重复 → 409；
  关联的 `SalesOrderLineId` 不存在 → 404。
- 订单联动：关联订单行且订单处于 `Confirmed` 时自动置为 `InProduction`（幂等，
  已是 `InProduction` / `Completed` 不再变更）；订单为 `Draft` → 409（"订单未确认。"），
  `Cancelled` → 409。
- 状态流转：非法流转一律 `ConflictException`（409），提示文案如
  "任务当前状态 {x} 不允许开始打印。"
- 完工：仅 `Printing` / `PostProcessing` 可完工；`quantity ≤ PlannedQuantity`；
  通过库存事务（Inbound 语义）写入流水，`DocumentNo = "{任务单号}-F-01"`，
  物料 = `OutputMaterialId`，仓库 = `OutputWarehouseId`，业务方 = null；
  库存侧主数据 / 防重 / 冲突沿用阶段 2 规则（重复记账 409，物料停用 404）。
  完工成功后 `CompletedQuantity = quantity`，任务 → `Completed`。
- 取消：终态不可取消；已完工任务不可取消；取消不回滚已发生耗用（耗用流水不可变）。
- 主键 `Guid.CreateVersion7()`，时间 UTC，`decimal` 金额数量。

## 6. 验收标准

1. 端点带 `WithName` / `WithSummary`，注册进 `Program.cs`。
2. 单元测试覆盖：校验器分支；全部非法状态流转 409；完工数量超计划 409；
   订单联动（Confirmed→InProduction 幂等、Draft 拒绝）；完工入库单据号格式。
3. `dotnet build 3dERP.slnx`、`dotnet test 3dERP.slnx` 通过。

## 7. 验证命令

```powershell
dotnet build 3dERP.slnx
dotnet test 3dERP.slnx
```
