# TASK-P3-02 · 销售订单应用服务与 API

| 字段 | 值 |
| --- | --- |
| 里程碑 | M3.1 销售订单后端 |
| 依赖 | TASK-P3-01 |
| 优先级 | 高 |
| 状态 | 待开发 |

## 1. 背景与目标

为销售订单提供用例层与 HTTP 端点：创建（草稿）、修改（仅草稿）、确认、取消、分页查询、详情。
endpoint 保持"薄"，业务规则集中在服务层，风格对齐 `CustomerService` / `CustomerEndpoints`。

## 2. 范围

### 做

- DTO、请求模型、校验器（手写，风格对齐 `CustomerRequestValidator`）。
- `ISalesOrderRepository` 接口与 `SalesOrderRepository` 实现（含事务性写入）。
- `SalesOrderService`（创建 / 更新 / 确认 / 取消 / 查询）。
- `SalesOrderEndpoints`（`/api/v1/sales-orders`）与 `Program.cs` 注册。
- 单元测试（fake repository）。

### 不做

- 发货（TASK-P3-06）、生产任务联动（TASK-P3-04）、前端（TASK-P3-07）。

## 3. 交付物

```text
src/3dERP.Application/Sales/SalesOrderDto.cs
src/3dERP.Application/Sales/SalesOrderRequests.cs        （Create / Update / Confirm / Cancel）
src/3dERP.Application/Sales/SalesOrderRequestValidator.cs
src/3dERP.Application/Sales/ISalesOrderRepository.cs
src/3dERP.Application/Sales/SalesOrderService.cs
src/3dERP.Infrastructure/Persistence/Repositories/SalesOrderRepository.cs
src/3dERP.Api/Endpoints/SalesOrderEndpoints.cs
src/3dERP.Api/Program.cs                                  （注册 validator / service）
tests/3dERP.UnitTests/Sales/SalesOrderRequestValidatorTests.cs
tests/3dERP.UnitTests/Sales/FakeSalesOrderRepository.cs
tests/3dERP.UnitTests/Sales/SalesOrderServiceTests.cs
```

## 4. API 设计

| 方法与路径 | 名称 | 说明 |
| --- | --- | --- |
| `GET /api/v1/sales-orders` | ListSalesOrders | 分页（page/pageSize），支持 `status`、`customerId`、`keyword`（单据号/客户名）过滤 |
| `GET /api/v1/sales-orders/{id}` | GetSalesOrderById | 含行；不存在返回 404 |
| `POST /api/v1/sales-orders` | CreateSalesOrder | 创建草稿单；返回 201 `CreatedAtRoute` |
| `PUT /api/v1/sales-orders/{id}` | UpdateSalesOrder | 仅 `Draft` 可改（整体替换行集合）；否则 409 |
| `POST /api/v1/sales-orders/{id}/confirm` | ConfirmSalesOrder | `Draft → Confirmed` |
| `POST /api/v1/sales-orders/{id}/cancel` | CancelSalesOrder | 允许 `Draft/Confirmed/InProduction`，否则 409 |

错误映射沿用全局 `ApiExceptionHandler`：`ValidationException` → 400，
`NotFoundException` → 404，`ConflictException` → 409。

## 5. 业务规则

### 校验器（400）

- `DocumentNo`：去除首尾空白后必填，2-64 位。
- `CustomerId` 必填；行数 1-50；每行 `LineNo` ≥ 1 且不重复；`MaterialId` 必填；
  `Quantity` > 0；`UnitPrice` ≥ 0。
- 提示文案使用中文，风格对齐阶段 2（如"销售订单号长度必须为 2-64 位。"）。

### 服务层（409）

- 创建：`DocumentNo` 已存在 → `ConflictException`（"销售订单号 {x} 已存在。"）；
  客户不存在或停用 → 404（"客户不存在或已停用。"）；物料不存在或停用 → 404。
- 确认：仅 `Draft` 可确认；确认时行集合非空。
- 取消：任一行 `ShippedQuantity > 0` → 409（"订单已发生发货，禁止取消。"）；
  存在未完成任务关联（P3-04 之后生效）→ 409。
- 更新：仅 `Draft`；并发冲突（rowversion）由 EF `DbUpdateConcurrencyException` 冒泡。
- 主键与审计：`Guid.CreateVersion7()`；`CreatedAtUtc/UpdatedAtUtc` 用 `DateTime.UtcNow`。
- 单据不做物理删除；查询默认排除已取消订单（`includeCancelled=true` 时返回）。

## 6. 验收标准

1. 端点全部带 `WithName` / `WithSummary`，Scalar 可见。
2. 单元测试覆盖：校验器全部错误分支；创建重复单据号 409；非草稿更新 409；
   确认 / 取消的正常与非法流转；分页与关键字过滤。
3. `dotnet build 3dERP.slnx`、`dotnet test 3dERP.slnx` 通过。

## 7. 验证命令

```powershell
dotnet build 3dERP.slnx
dotnet test 3dERP.slnx
```
