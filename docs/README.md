# 3dERP 文档

规划与交付文档索引。工程约定（金额 `decimal`、UTC 时间、`Guid.CreateVersion7()`、乐观并发、
单据不物理删除、库存以流水为准等）见根目录 `README.md`，文档中的所有任务默认遵守这些约定。

## 目录结构

```text
docs/
  README.md               本索引
  phase-3/
    plan.md               第三阶段总体计划：目标、范围、里程碑、风险
    milestones.md         里程碑定义与出口条件
    tasks/                任务文档：每个任务一份，含范围、交付物、业务规则与验收标准
```

## 阶段进度

| 阶段 | 内容 | 状态 |
| --- | --- | --- |
| 阶段 1 | 主数据：客户 / 供应商 / 物料 / 仓库 | 已完成 |
| 阶段 2 | 库存流水、余额与预警规则 | 已完成 |
| 阶段 3 | 销售订单到生产任务的闭环 | 规划完成，待开发 |
| 阶段 4 | 权限、审计、编号规则与集成测试补全 | 未开始 |

## 第三阶段任务索引

来源：backlog 项 `01a1dee4-137e-78b4-bce2-66dfd0683a5d`（阶段 3 规划获批）。

| 任务 | 里程碑 | 标题 | 依赖 |
| --- | --- | --- | --- |
| [TASK-P3-01](phase-3/tasks/TASK-P3-01-sales-order-domain.md) | M3.1 | 销售订单领域模型与持久化 | 无 |
| [TASK-P3-02](phase-3/tasks/TASK-P3-02-sales-order-api.md) | M3.1 | 销售订单应用服务与 API | P3-01 |
| [TASK-P3-03](phase-3/tasks/TASK-P3-03-production-task-domain.md) | M3.2 | 生产任务领域模型与持久化 | P3-01 |
| [TASK-P3-04](phase-3/tasks/TASK-P3-04-production-task-api.md) | M3.2 | 生产任务应用服务与 API（含完工入库） | P3-02、P3-03 |
| [TASK-P3-05](phase-3/tasks/TASK-P3-05-production-consumption.md) | M3.2 | 生产材料耗用（库存出库联动） | P3-04 |
| [TASK-P3-06](phase-3/tasks/TASK-P3-06-sales-shipment.md) | M3.3 | 销售发货与订单闭环 | P3-02 |
| [TASK-P3-07](phase-3/tasks/TASK-P3-07-sales-frontend.md) | M3.4 | 销售订单前端页面 | P3-02、P3-06 |
| [TASK-P3-08](phase-3/tasks/TASK-P3-08-production-frontend.md) | M3.4 | 生产任务前端页面 | P3-04、P3-05 |
| [TASK-P3-09](phase-3/tasks/TASK-P3-09-closed-loop-integration-tests.md) | M3.5 | 闭环集成测试与阶段验收 | 全部 |

各里程碑的出口条件见 [milestones.md](phase-3/milestones.md)。

## 验证命令

所有任务完成后至少执行：

```powershell
dotnet build 3dERP.slnx
dotnet test 3dERP.slnx
```

涉及前端页面的任务另需在 `web/` 下执行 `npm run build` 验证构建。
