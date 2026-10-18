# 独立应用 {{NAMESPACE}}

本目录包含元数据、强类型 Entity/DTO/VO/Mapper/Service/Controller、Core/Data 源码和 ASP.NET Core 宿主。普通 CRUD 不依赖在线 Metarill/YZP。首期数据库为 MySQL，启动不会自动创建或修改表。

## 构建与结构同步

安装 global.json 指定的 .NET SDK，在包根目录执行：

```powershell
dotnet restore backend/Application.Host/Application.Host.csproj
dotnet build backend/Application.Host/Application.Host.csproj --no-restore
```

SDK 与直接 NuGet 依赖版本已固定；首次 restore 生成各项目 packages.lock.json，应提交这些锁文件，后续 CI 使用 `dotnet restore --locked-mode`，锁定传递依赖。Core/Data 仅包含允许的源码及工程文件，原机器路径和私有配置不会复制。

通过环境变量设置 Database__Provider=mysql、Database__ConnectionString、Database__ExpectedDatabase，或在宿主目录自行创建已忽略的 appsettings.local.json。不要把真实凭据提交到仓库。

```powershell
dotnet run --project backend/Application.Host -- --schema-preview
# 确认变更后才执行：同一进程重新预览并执行，记录 .local/schema-history.jsonl。
dotnet run --project backend/Application.Host -- --schema-apply
dotnet run --project backend/Application.Host --urls http://127.0.0.1:5080
```

只对包内元数据表进行结构同步。MySQL DDL 可能已经部分生效，失败后检查记录再预览，不承诺 DDL 事务回滚。

## 认证

默认不开放任何 API。`/health` 仅报告进程状态，不代表数据库或业务验收通过。正式部署应在同程序集手写 partial `HostCustomization.ConfigureServices` 中替换 `IRuntimeAuthorization`，身份和项目必须来自服务端认证。

开发测试可显式设置 ASPNETCORE_ENVIRONMENT=Development，以及 Authentication__DevelopmentBootstrap__Enabled=true、Username、Password（至少12位），然后启动宿主。账号仅拥有包内项目权限；项目 ID 不能由客户端切换。开发认证使用内存会话、固定到期时间、令牌摘要和登录限流，进程重启会失效；仅用于本地开发，不作为正式账号体系。

POST `/api/admin/sys/yz_manager/login`，JSON 或表单包含 username/password；成功响应 map.loginStateId，后续在 loginStateId 请求头传递令牌（也可 Bearer）。POST `/api/admin/sys/yz_manager/exit_login` 使当前会话失效。通过 SessionLifetimeSeconds 配置 1..86400 秒有效期。

## 业务扩展与边界

生成服务 `.g.cs` 与手写 `.cs` 使用 partial；BeforeSave 修改派生字段后必须加入 submittedFields。write-schema 列出每页允许提交的字段，服务端拒绝未知和审计字段，省略与显式 null 含义不同。表范围、权限、ID、审计和事务在服务端执行。

本包包含当前已实现的普通 CRUD、复制、JSON 行导入及查询导出投影。文件库、导出票据/下载、Agent、平台设计器和复杂自定义 SQL 未随包提供。未接入文件库时写入非空文件引用会回滚；导出调用明确返回不可用。正式宿主需替换 IRecordMutationObserver/IPageExportService 接口实现。

backend/generated/frontend 仅为复用已有 uni-app 组件和共享运行时的页面模板，不是完整可独立运行的 uni-app 工程。控件未移植项见 generation-capabilities.json，不能用后端构建代替 Web、小程序或 App 验收。

元数据已删除已知敏感配置键及密码列默认值；发布前仍需审核业务定义中是否存在以非标准名称保存的凭据。包内 .metarill-package.json 记录文件摘要；生成目录 .metarill-generation.json 保护后续生成归属。
