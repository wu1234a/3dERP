# 3dERP

面向 3D 打印业务的 ERP。当前仓库是**可运行的骨架**：后端、前端、数据库连接、健康检查和 OpenAPI 已经打通，业务模块逐步实现。

## 技术栈

- 后端：.NET 10、ASP.NET Core Minimal API、EF Core 10、SQL Server
- 前端：Vue 3、Vite、Pinia、Vue Router、Axios、Tailwind CSS
- 数据库：SQL Server 2022+（开发库 `3dERP_Dev`，测试库 `3dERP_Test`）
- 测试：xUnit、WebApplicationFactory（后续补集成测试）

## 目录

```text
src/
  3dERP.Api/             HTTP 入口、OpenAPI、健康检查
  3dERP.Application/     用例与 DTO
  3dERP.Domain/          领域模型与规则
  3dERP.Infrastructure/  EF Core、SQL Server、外部集成
tests/
  3dERP.UnitTests/
  3dERP.IntegrationTests/
web/                     Vue 3 管理端
```

## 本地启动

### 数据库

创建开发库：

```sql
CREATE DATABASE [3dERP_Dev];
CREATE DATABASE [3dERP_Test];
```

连接字符串在 `src/3dERP.Api/appsettings.Development.json`。

### 后端

```powershell
dotnet build 3dERP.slnx
dotnet run --project src/3dERP.Api --no-launch-profile
```

- API：`http://localhost:5098`
- 健康检查：`http://localhost:5098/health`
- OpenAPI 调试页：`http://localhost:5098/scalar/v1`

### 前端

```powershell
cd web
npm install
npm run dev
```

- 管理端：`http://localhost:5173`
- Vite 会把 `/api` 和 `/health` 代理到 `http://localhost:5098`

## 领域模块

- 主数据：客户、供应商、物料、仓库、计量单位、工艺参数
- 销售：报价、销售订单、发货、应收
- 采购：采购申请、采购订单、收货、应付
- 生产：3D 打印任务、排产、材料耗用、设备、后处理
- 库存：库存流水、调拨、盘点、可用量
- 财务：总账、应收、应付、凭证
- 系统：用户、角色、权限、编号规则

## 工程约定

- 金额使用 `decimal`，禁止 `double`
- 时间统一 UTC 存储
- 主键使用 `Guid.CreateVersion7()`
- 关键表使用乐观并发令牌
- 业务单据不做物理删除
- 库存以流水为准，余额由流水汇总
- 数据库变更通过 EF Core Migrations 管理
- 前端只使用 JavaScript，不使用 TypeScript

## 下一步

1. 建立主数据模块（客户 / 物料 / 仓库）
2. 建立库存流水与可用量
3. 建立销售订单到生产任务的闭环
4. 补权限、审计和集成测试
