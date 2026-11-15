# 第三阶段里程碑

每个里程碑是一个可独立评审、可合并的交付单元。出口条件全部满足才算完成。

## M3.1 销售订单后端

| 字段 | 值 |
| --- | --- |
| 包含任务 | TASK-P3-01、TASK-P3-02 |
| 依赖 | 无（基于阶段 1 主数据） |
| 交付分支建议 | `feat/phase3-sales-orders` |

### 出口条件

1. 新增迁移 `AddSalesOrders`（`SalesOrders` / `SalesOrderLines`），可直接
   `dotnet ef database update` 应用到 `3dERP_Dev`。
2. `/api/v1/sales-orders` 端点组在 `Program.cs` 注册完成，Scalar/OpenAPI 可见且
   每个端点带 `WithSummary` 中文摘要。
3. 单元测试覆盖：请求校验器全部错误分支、状态机全部非法流转（409）、
   创建 / 确认 / 取消 / 分页查询的正常路径。
4. `dotnet build 3dERP.slnx` 与 `dotnet test 3dERP.slnx` 通过。

## M3.2 生产任务后端

| 字段 | 值 |
| --- | --- |
| 包含任务 | TASK-P3-03、TASK-P3-04、TASK-P3-05 |
| 依赖 | M3.1（生产任务可关联销售订单行） |
| 交付分支建议 | `feat/phase3-production-tasks` |

### 出口条件

1. 新增迁移 `AddProductionTasks`（`ProductionTasks` / `ProductionConsumptions`）。
2. `/api/v1/production-tasks` 端点组可用，支持创建、开始、后处理、完工、取消。
3. 材料耗用登记写入库存 Out 流水（`{任务单号}-C-{序号}`），完工登记写入库存 In 流水
   （`{任务单号}-F-01`）；库存不足 / 单据号冲突返回 409 且任务数据不落库。
4. 关联订单行的任务创建后，订单自动 `Confirmed → InProduction`（幂等）。
5. 单元测试覆盖状态机、耗用校验、完工校验与订单状态联动；`dotnet build` / `dotnet test` 通过。

## M3.3 发货与订单闭环

| 字段 | 值 |
| --- | --- |
| 包含任务 | TASK-P3-06 |
| 依赖 | M3.1（可独立于 M3.2 并行交付） |
| 交付分支建议 | `feat/phase3-sales-shipments` |

### 出口条件

1. 新增迁移 `AddSalesShipments`（`SalesShipments` / `SalesShipmentLines`）。
2. `POST /api/v1/sales-orders/{id}/ship` 支持一次发货多行；发货单、行、
   `ShippedQuantity` 更新与库存 Out 流水在同一事务内提交，任一行库存不足整单回滚。
3. 全部行发足后订单自动 `Completed`；超发、重复发货序号、非法状态发货返回 409。
4. 单元测试覆盖部分发货、分次发货、超发拒绝、库存不足整单回滚；`dotnet build` / `dotnet test` 通过。

## M3.4 前端接入

| 字段 | 值 |
| --- | --- |
| 包含任务 | TASK-P3-07、TASK-P3-08 |
| 依赖 | M3.1、M3.3（销售页）；M3.2（生产页） |
| 交付分支建议 | `feat/phase3-frontend` |

### 出口条件

1. `/sales`、`/production` 路由不再使用 `ModuleView` 占位，替换为真实列表 + 操作页。
2. 页面仅使用 JavaScript（不引入 TypeScript），样式沿用 Tailwind 约定，API 调用走
   `src/api/http.js` 封装。
3. 覆盖各自任务文档中列出的页面操作与错误提示；`npm run build` 通过。

## M3.5 闭环集成测试与阶段验收

| 字段 | 值 |
| --- | --- |
| 包含任务 | TASK-P3-09 |
| 依赖 | M3.1 ~ M3.4 全部完成 |
| 交付分支建议 | `feat/phase3-closed-loop-tests` |

### 出口条件

1. 闭环集成测试（WebApplicationFactory + 内存/测试库）跑通完整链路：
   主数据 → 订单 → 确认 → 生产任务 → 领料 → 完工入库 → 发货出库 → 订单完成，
   并断言各步骤的库存流水与余额。
2. 负向用例（超发、库存不足、重复单据号、非法状态流转）全部返回 409。
3. `dotnet test 3dERP.slnx` 全绿；根 `README.md` 的《下一步》段落更新为阶段 3 已完成、
   指向阶段 4。
4. `docs/README.md` 阶段进度表更新为"已完成"。
