# MySQL 首版安装与恢复

本页说明 Metarill 平台及独立 Full 的发行目录、首次安装和恢复。首版面向桌面与移动 Web。安装前核对[首版签收清单](mysql-first-release-milestone.md)中的固定提交、验收范围和待办。

## 发行目录

| 内容 | 平台 | 独立 Full |
| --- | --- | --- |
| 启动工作目录 | 平台发行根 | 应用发行根 |
| 后端 | `host/Metarill.Api.dll` 与同次发布的全部依赖 | `host/Application.Host.dll` 与同次发布的全部依赖 |
| 固定元数据 | 发行根的 `global.json` 和 `backend/metadata/{platform,demo,files,security}.json`，与宿主来源同提交 | `host/metadata/{project,files,security}.json` 随包发布，Agent 基础目录由同一个程序集提供 |
| 私有配置 | `METARILL_SETTINGS` 指向私有 JSON | 发行根的 `appsettings.local.json` 或对应环境变量 |
| 持久目录 | 发行根的 `.local/files`、`.local/exports`、`.local/schema-history.jsonl` | `Files:RootDirectory`、`Exports:RootDirectory` 和发行根的 `.local/schema-history.jsonl` |

将同一提交的完整公开源码导出到平台发行根，例如用 Git 归档解包到该根目录。平台在线生成从发行根读取后端模板、公共运行时源码、原前端资产及构建清单，因此应保留归档中的源码布局。独立 Full 先从已审核的公开元数据生成到空目录，再发布包内 `backend/Application.Host`。为目标系统选用对应运行时标识，安装与发布物匹配的 ASP.NET Core 运行时。下面以 Linux x64 为例，两条命令分别从各自源码根执行，输出到各自的发行根 `host` 目录。

```bash
# 平台源码根
dotnet publish backend/src/Metarill.Api/Metarill.Api.csproj -c Release -r linux-x64 --self-contained false -o /opt/metarill/platform/host

# 独立 Full 源码根
dotnet publish backend/Application.Host/Application.Host.csproj -c Release -r linux-x64 --self-contained false -o /opt/metarill/app/host
```

将平台四份元数据及 `global.json` 按表格布置。发布后核对全部文件摘要，包括依赖和元数据。发行物和 Web 静态目录与私有配置、数据库备份、文件库及导出临时文件分开。部署配置由服务账户读取，持久目录由运行账户写入。

## 首次安装

1. 为平台库、业务库及独立应用库配置明确的 MySQL 目标和库级授权，确保连接串的 `Database` 与 `ExpectedDatabase` 一致。平台按 [示例配置](../backend/appsettings.Example.json)设置 `Metarill:Database`、`Metarill:BusinessDatabase`。额外业务项目用服务端的 `Metarill:ProjectDatabases` 绑定。独立 Full 设置顶层 `Database`。连接参数见 [MySQL 严格 TLS](mysql-connection-tls.md)。
2. 配置 Redis DB0、初始化用户名及随机密码。平台配置 `Metarill:Authentication:LoginFailures:ConnectionString` 和 `Metarill:Bootstrap`。独立 Full 配置 `Authentication:Mode=Web`、`Authentication:LoginFailures:ConnectionString` 和 `Authentication:Bootstrap`。多后台应用另指定 `Authentication:Bootstrap:AdminTypeId`。不同进程分配独立的 `Metarill:WorkerId` 或 `Ids:WorkerId`。
3. 核对目标库归属后，从相应的发行根执行命令。平台首次 `--initialize` 显式同步固定基础结构并补种子。独立 Full 先核对结构预览及目标库，再应用计划并初始化安全表中的项目、后台和账号。普通宿主启动只启动服务。
4. 用原登录页完成真实密码登录和菜单读取，再做目标业务库的一次新增、回读、逻辑删除和注销。健康接口分别为平台 `/api/runtime/health/check` 和 Full `/health`。Redis、真实账号和业务库通过登录与业务操作验证。

```bash
# 平台发行根
cd /opt/metarill/platform
export ASPNETCORE_ENVIRONMENT=Production
export METARILL_SETTINGS=/etc/metarill/platform.json
dotnet host/Metarill.Api.dll --initialize
dotnet host/Metarill.Api.dll --urls http://127.0.0.1:8088
```

```bash
# 独立应用发行根
cd /opt/metarill/app
export ASPNETCORE_ENVIRONMENT=Production
dotnet host/Application.Host.dll --schema-preview
# 审核预览和目标库后继续。
dotnet host/Application.Host.dll --schema-apply
dotnet host/Application.Host.dll --initialize-security
dotnet host/Application.Host.dll --urls http://127.0.0.1:5080
```

已有管理员密码由账号流程维护。两种宿主的显式初始化重复执行会保留现有账号密码。确认本次安装回执后，从正式运行配置移除仅用于引导的密码。

## Web 与网关

用各工程自己的 AppID、锁定的前端依赖和 HBuilderX 导出 Web。平台在部署域名使用同源 `/api`。独立 Full 将 `frontend/common/application.config.js` 的 API 地址填为自己的 HTTPS 站点地址并以 `/api` 结尾。网关只发布 H5 静态目录，并将原 `/api` 路径转发到对应的私有宿主。文件下载、上传和 Agent／MCP 如需开放，按其协议及权限验证，并保留文件根在静态目录之外。 [可信反向代理](web-reverse-proxy.md) · [独立应用配置](standalone-application.md) · [Tiny Cloud](tiny-cloud.md)

上线前用实际域名与证书链分别检查桌面和移动 Web。至少验证有效证书、原登录及注销、相同来源的 API、生成业务 CRUD、公开与受保护文件、未授权请求和网关实际看到的客户端地址。记录服务器、发行物摘要和操作时间。

## 更新与失败恢复

平台分库加本地附件的隔离恢复顺序已在 [V630](testing/current-split-backup-restore-v630.md) 实测。停止源写入后分别取得平台库、业务库及附件目录的同点备份。为恢复目标分配空库、专属账号、Redis 和持久目录，用目标库级账号导入对应的两个库，再按文件键复制附件。以同版宿主和元数据指向恢复库，通过正常启动命令验证真实登录、文件引用、下载字节及权限。复导出的 DDL 如有文本差异，应核对真实列定义、排序规则、索引和约束，再核对所有数据语句。

跨 MySQL 实例恢复见 [V632](testing/current-cross-instance-backup-restore-v632.md)。先分别读取源、目标的主机名及实例 UUID，核对目标空库名称及导入账号归属。两份备份按平台库和业务库分别导入，可使用目标的新库名。宿主私有配置使用目标连接和对应的 `ExpectedDatabase`。恢复后从目标独立核对客户、文件与引用，再通过原接口验证账号、权限和会话。

S3 对象恢复见 [V631](testing/current-split-s3-backup-restore-v631.md)。停写后备份 MySQL 与对应对象键，按清单核对目标空桶，写回相同键并独立读取实际大小和 SHA-256。原文件定位要求保留原配置别名、规范化端点、区域、桶、前缀和寻址方式，凭据可独立轮换。恢复服务可用同一逻辑地址指向物理目标，再核对公开和受保护内容的原权限。直接更换逻辑端点会拒绝旧引用，涉及改名的迁移应单独核对文件定位。 [S3 定位规则](s3-object-storage.md)

- 变更前保存上一发行物及其摘要，备份本次影响的配置库、业务库、持久文件和远端对象存储。先在隔离目标恢复备份，并复核表数据和文件引用是否对应。
- 已有生成项目用 [受管源码更新](local-generation-update.md) 先预览再应用，保留手写 partial、AppID 和私有设置。结构变更单独核对目标与差异，按“预览、执行、核验、记录”处理。首次安装命令按本页的空库场景使用。
- 如果只有应用包发生变化，停写后核对旧包与现行数据库结构的兼容性，再恢复同一版宿主、元数据和 H5。若结构或数据已变化，按已验证的备份把数据库和文件恢复到独立目标后再切换。MySQL DDL 可隐式提交，部分成功时先保留执行回执和现场，重新反查后决定继续或恢复。恢复后复验真实登录、目标库写入、文件引用及注销。
- 本首版按新部署的 MySQL 平台和新生成应用验收。原 YZP Java 存量库及业务源码的迁移另行评估。

当前固定候选的空库安装、重复安装和 HTTP 会话见 [V613](testing/current-linux-candidate-runtime-v613.md)。平台桌面和独立 Full 移动 Web 的真实浏览器直连 HTTPS 见 [V615](testing/current-browser-direct-https-v614-v615.md)。实际部署域名及其网关按部署环境单独留证。

V618 从 `5c8fcb82` 验证平台发行根的 2,841 份公开源码与 49 份 Linux 宿主文件，以及独立 Full 的 49 份发布物。CLI 以该发行根作为来源两次生成 659 文件的示例 Full，逐份一致，生成宿主严格构建通过。该目录核对与 V613／V615 的实际安装和浏览器验收分列。
