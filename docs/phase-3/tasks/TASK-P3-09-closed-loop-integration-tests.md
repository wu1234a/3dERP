# TASK-P3-09 · 闭环集成测试与阶段验收

| 字段 | 值 |
| --- | --- |
| 里程碑 | M3.5 闭环集成测试与阶段验收 |
| 依赖 | M3.1 ~ M3.4 全部任务 |
| 优先级 | 高 |
| 状态 | 待开发 |

## 1. 背景与目标

用 WebApplicationFactory 集成测试验证"销售订单 → 生产任务 → 领料 → 完工入库 →
发货出库 → 订单完成"整条闭环，并完成阶段收尾（文档与 README 更新）。

## 2. 范围

### 做

- 销售与生产两组 API 集成测试（补齐 TASK-P3-02 / P3-04 / P3-05 / P3-06 的端到端覆盖）。
- 闭环测试：正向全链路 + 负向分支。
- 阶段收尾文档更新。

### 不做

- 性能 / 压力测试；阶段 4 的权限与审计测试。

## 3. 交付物

```text
tests/3dERP.IntegrationTests/Sales/SalesApiFactory.cs
tests/3dERP.IntegrationTests/Sales/SalesOrdersEndpointsTests.cs
tests/3dERP.IntegrationTests/Sales/SalesShipmentEndpointsTests.cs
tests/3dERP.IntegrationTests/Production/ProductionApiFactory.cs
tests/3dERP.IntegrationTests/Production/ProductionTaskEndpointsTests.cs
tests/3dERP.IntegrationTests/ClosedLoop/ClosedLoopEndpointsTests.cs
README.md                              （《下一步》更新为阶段 4）
docs/README.md                         （阶段进度表更新）
```

## 4. 测试用例

### 正向闭环（ClosedLoopEndpointsTests）

1. 经 API 建立主数据：客户、原料物料、成品物料、启用仓库 × 2。
2. 原料入库（In，`PO-2026-0001`，数量 100）。
3. 创建销售订单（2 行，草稿）→ 确认。
4. 创建生产任务（关联行 1，产出成品物料）→ 订单自动 `InProduction`。
5. 登记耗用（原料出库 40）→ 断言原料余额 60、流水 `PT-*-C-01`。
6. 完工（成品入库 40）→ 断言成品余额 40、流水 `PT-*-F-01`、任务 `Completed`。
7. 发货（行 1 发 40 + 行 2 直接发货）→ 断言成品余额 0、流水 `SO-*-S-01`、
   行 `ShippedQuantity`、订单 `Completed`。

### 负向分支

| 用例 | 期望 |
| --- | --- |
| 重复订单 / 任务单据号 | 409 |
| 非草稿修改订单 / 非法状态流转 | 409 |
| 耗用时库存不足 | 409，且无耗用明细落库 |
| 超发 / 库存不足发货 | 409，整单回滚（余额与 ShippedQuantity 不变） |
| 已发货订单取消 | 409 |
| 停用主数据参与业务 | 404 |

### 既有约定回归

- 流水不可变、`(MovementType, DocumentNo)` 唯一、余额与流水一致。
- 所有时间字段为 UTC（断言 `Kind` / 序列化值）。

## 5. 验收标准（阶段出口）

1. `dotnet test 3dERP.slnx` 全绿（单元 + 集成）。
2. 根 `README.md`《下一步》更新：阶段 3 完成情况与阶段 4 目标（权限、审计、编号规则）。
3. `docs/README.md` 阶段进度表将阶段 3 标记为"已完成"。
4. 集成测试不依赖外部 SQL Server 时使用现有 `InventoryApiFactory` 同款替换策略；
   依赖真实库的用例（并发 / rowversion）标注 `[Trait]` 便于 CI 跳过。

## 6. 验证命令

```powershell
dotnet build 3dERP.slnx
dotnet test 3dERP.slnx
```
