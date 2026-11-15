# TASK-P3-05 · 生产材料耗用（库存出库联动）

| 字段 | 值 |
| --- | --- |
| 里程碑 | M3.2 生产任务后端 |
| 依赖 | TASK-P3-04 |
| 优先级 | 高 |
| 状态 | 待开发 |

## 1. 背景与目标

打印耗用原料时，从仓库扣减物料并留下不可变库存流水。复用阶段 2 的
`InventoryTransactionService` 语义（禁止负库存、单据号防重、余额 + 流水同事务），
在任务维度记录耗用明细。

## 2. 范围

### 做

- `ProductionTaskService.RecordConsumptionAsync`（登记耗用 → 库存 Out 流水）。
- 端点：`POST /api/v1/production-tasks/{id}/consumptions`、
  `GET /api/v1/production-tasks/{id}/consumptions`。
- 单元测试。

### 不做

- 耗用成本核算与财务凭证（财务模块，后续阶段）。
- 退料 / 冲销（本期通过备注说明，负向调整走阶段 2 的 Adjustment）。

## 3. 交付物

```text
src/3dERP.Application/Production/ProductionConsumptionDto.cs
src/3dERP.Application/Production/RecordConsumptionRequest.cs
src/3dERP.Application/Production/RecordConsumptionRequestValidator.cs
src/3dERP.Application/Production/ProductionTaskService.cs          （扩展耗用方法）
src/3dERP.Application/Production/IProductionTaskRepository.cs      （扩展查询）
src/3dERP.Infrastructure/Persistence/Repositories/ProductionTaskRepository.cs
src/3dERP.Api/Endpoints/ProductionTaskEndpoints.cs
tests/3dERP.UnitTests/Production/RecordConsumptionRequestValidatorTests.cs
tests/3dERP.UnitTests/Production/ProductionConsumptionTests.cs
```

## 4. API 设计

| 方法与路径 | 名称 | 说明 |
| --- | --- | --- |
| `POST /api/v1/production-tasks/{id}/consumptions` | RecordProductionConsumption | 登记耗用，201 返回耗用明细与流水摘要 |
| `GET /api/v1/production-tasks/{id}/consumptions` | ListProductionConsumptions | 任务耗用明细列表 |

请求体：`{ "materialId": "...", "quantity": 3.2, "note": "打印 1# 零件" }`。

## 5. 业务规则

- 仅 `Printing` / `PostProcessing` 状态可登记耗用，否则 409。
- `quantity` > 0；物料必须存在并启用（404）。
- 单据号：流水 `DocumentNo = "{任务单号}-C-{序号:00}"`，序号 = 任务内已有最大序号 + 1，
  写入与流水在同一事务；`(MovementType, DocumentNo)` 唯一索引兜底重复。
- 库存不足：沿用阶段 2 规则返回 409
  （"库存不足：当前现有量 {x}，出库数量 {y}。"），且任务明细不落库。
- 明细记录 `InventoryMovementId` 供追溯；流水不可变，冲销走库存调整。
- 时间 UTC；数量 `decimal(19,4)`；主键 `Guid.CreateVersion7()`。

## 6. 验收标准

1. 耗用后：库存余额减少、Out 流水 `DocumentNo` 形如 `PT-2026-0001-C-01`、
   明细可查询且含 `InventoryMovementId`。
2. 连续登记序号递增；库存不足 / 任务状态非法 / 物料停用时分别 409 / 409 / 404。
3. 单元测试覆盖以上分支；`dotnet build 3dERP.slnx`、`dotnet test 3dERP.slnx` 通过。

## 7. 验证命令

```powershell
dotnet build 3dERP.slnx
dotnet test 3dERP.slnx
```
