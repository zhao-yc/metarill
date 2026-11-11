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

收到固定候选 ZIP 时，先按同目录的 `SHA256SUMS` 核对归档摘要，再解包到各自的新目录。Linux 可以在下载目录执行 `sha256sum --check SHA256SUMS`，Windows 可用 `Get-FileHash -Algorithm SHA256` 逐包核对。内置 `release-manifest.json` 列出源码提交、角色、运行目标与所有载荷摘要。按本页从解包后的发行根用 `dotnet` 启动宿主。最近通过双包复建与三种隔离安装的候选为 `e682cd12`。实际回执见 [V1125／V1126 排序候选](testing/storage-mode-linux-release-v1009-v1010.md#v1125-与-v1126-排序候选独立双包及三拓扑)。当前候选的安装文件、配置样例及数据库能力边界见 [V1143 当前双包分发核对](testing/current-release-distribution-v963.md#v1143-当前双包分发与数据库边界)。部署新提交时按 [首版清单](mysql-first-release-milestone.md) 核对该版本的验收状态。

### 从固定提交构建发行包

先检出[首版清单](mysql-first-release-milestone.md)中要构建的候选提交，再从该检出执行下列命令。构建需要 Git、Python 3.10 及以上，以及 `global.json` 指定的 .NET SDK。输出目录须是上级目录已存在的新目录。Windows 和 Linux 上的构建目标均为 Linux x64。

```bash
git rev-parse --verify HEAD
python scripts/build-release.py build --commit HEAD --output metarill-linux-candidate
python scripts/build-release.py verify --output metarill-linux-candidate
```

确认第一条命令打印的完整 SHA 与拟交付的候选一致，再开始构建。构建工具解析并记录该 SHA，校验输出中的两个包角色和同一源码版本。

构建只读取指定 Git 提交，独立示例 Full 从同一份元数据生成两次并核对。项目程序集的 ProductVersion 使用该目标提交。编译保留固定路径和调试符号，并关闭上级 Git 检出的源码链接。两份 ZIP 均含 Linux x64 宿主、源码、许可及逐文件清单。网站静态文件按下一节的部署配置单独构建。离线环境可通过 `--nuget-source` 指定已经包含所需包的本地目录，非默认 SDK 路径可通过 `--dotnet` 指定。拿到发行文件后，先从可信交付渠道取得对应的 `SHA256SUMS`，再用 `verify` 核对外层摘要、包内路径、源码提交和文件内容。

## 首次安装

1. 为平台库、业务库及独立应用库配置明确的 MySQL 目标和库级授权，确保连接串的 `Database` 与 `ExpectedDatabase` 一致。平台按 [示例配置](../backend/appsettings.Example.json)设置 `Metarill:Database`、`Metarill:BusinessDatabase`。额外业务项目用服务端的 `Metarill:ProjectDatabases` 绑定。独立 Full 以 `backend/Application.Host/appsettings.example.json` 为样例，将私有配置保存为发行根的 `appsettings.local.json`，设置顶层 `Database`。连接参数见 [MySQL 严格 TLS](mysql-connection-tls.md)。
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

独立 Full 使用只读容器根文件系统时，将顶层 `Exports:RootDirectory` 指向应用进程可写的私有挂载。也可用 `Exports__RootDirectory` 明确指定该路径。沿用基础镜像时同时核对其继承的环境变量。以运行账户验证可创建及删除临时文件，再用原页面导出并实际下载一次。见 [V657 原 Web 复验](testing/current-original-web-excel-v656-v657.md)。

已有管理员密码由账号流程维护。两种宿主的显式初始化重复执行会保留现有账号密码。确认本次安装回执后，从正式运行配置移除仅用于引导的密码。 分库平台、共库平台及独立 Full 已实测移除引导密码后重启。已有会话继续可用，管理员可用原账号密码登录。[V1109 移除引导密码复验](testing/storage-mode-linux-release-v1009-v1010.md#v1109-当前发行包移除引导密码后重启)。

由平台下载的独立 Full 可按包内配置执行同一安装流程。当前候选已核对实际返回 ZIP、严格构建、空库预览和显式安装，并从生成的新表原接口读写。[V1054 当前生成下载与独立安装](testing/current-generated-delivery-v917.md#v1054-当前发行包配置下载及独立安装)。

## Web 与网关

解包后，平台前端工程在发行根的 `frontend/metarill_uni_admin`，独立 Full 在发行根的 `frontend`。先按下文配置 AppID 和 API 地址，再在对应工程目录用 `npm ci --omit=optional` 按锁文件安装依赖。用本机 HBuilderX CLI 执行 `publish web --project "<前端工程绝对路径>" --webTitle "<站点标题>" --webHosting false`。核对 CLI 的编译成功和导出 Web 成功回执，再部署前端工程下 `unpackage/dist/build/web`。当前候选的平台与独立 Full 实际 ZIP 构建回执见 [V1140 双包 Web 发行构建](testing/current-release-distribution-v963.md#v1140-当前候选双包-web-发行构建)。

使用各工程自己的 AppID。平台在部署域名使用同源 `/api`。独立 Full 将 `frontend/common/application.config.js` 的 API 地址填为自己的 HTTPS 站点地址并以 `/api` 结尾。网关只发布 H5 静态目录，并将原 `/api` 路径转发到对应的私有宿主。当代理保留客户端 Host 且宿主启用 `AllowedHosts` 时，把实际部署域名加入顶层允许列表。独立 Full 示例默认只允许本机地址。域名列表使用分号分隔，填写主机名即可。文件下载、上传和 Agent／MCP 如需开放，按其协议及权限验证，并保留文件根在静态目录之外。 [可信反向代理](web-reverse-proxy.md) · [独立应用配置](standalone-application.md) · [Tiny Cloud](tiny-cloud.md)

启用 Agent 时，在平台私有配置中设置 `Metarill:Agent:VerificationUri`，独立 Full 设置 `Agent:VerificationUri`。使用各自部署域名的完整 HTTPS 授权页地址，例如 `https://app.example.com/#/pages/custom/agent_authorize`。从实际 OAuth 设备链接验收登录回跳、批准和撤销，确认链接指向本应用。

上线前用实际域名与证书链分别检查桌面和移动 Web。至少验证有效证书、原登录及注销、相同来源的 API、生成业务 CRUD、公开与受保护文件、未授权请求和网关实际看到的客户端地址。记录服务器、发行物摘要和操作时间。

## 更新与失败恢复

平台与业务共库部署停写后，备份该库及持久文件目录。恢复到目标空库后，把平台的 `Metarill:Database` 和 `Metarill:BusinessDatabase` 指向同一个恢复库，分别核对连接串和 `ExpectedDatabase`。业务数据、身份表和文件引用使用同一份数据库快照。恢复后的旧会话与授权处置在该库执行一次，再启动应用核对真实登录、原业务记录及附件权限。[V1112 平台共库跨实例恢复](testing/current-split-recovery-v927-v928.md#v1112-当前候选平台共库跨实例恢复)。

平台分库加本地附件的隔离恢复顺序已在 [V630](testing/current-split-backup-restore-v630.md) 实测。停止源写入后分别取得平台库、业务库及附件目录的同点备份。为恢复目标分配空库、专属账号、Redis 和持久目录，用目标库级账号导入对应的两个库，再按文件键复制附件。以同版宿主和元数据指向恢复库，通过正常启动命令验证真实登录、文件引用、下载字节及权限。复导出的 DDL 如有文本差异，应核对真实列定义、排序规则、索引和约束，再核对所有数据语句。

当前候选的同版跨 MySQL 实例恢复见 [V1041](testing/current-split-recovery-v927-v928.md#v1041v1042-当前发行候选同版分库恢复)。先分别读取源、目标的主机名及实例 UUID，核对目标空库名称及导入账号归属。两份备份按平台库和业务库分别导入，可使用目标的新库名。宿主私有配置使用目标连接和对应的 `ExpectedDatabase`。恢复后从目标独立核对客户、文件与引用，再通过原接口验证账号、权限和会话。

当前候选的同实例分库 SeaweedFS 恢复见 [V1042](testing/current-split-recovery-v927-v928.md#v1041v1042-当前发行候选同版分库恢复)。停写后备份 MySQL 与对应对象键，按清单核对目标空桶，写回相同键并独立读取实际大小和 SHA-256。原文件定位要求保留原配置别名、规范化端点、区域、桶、前缀和寻址方式，凭据可独立轮换。恢复服务可用同一逻辑地址指向物理目标，再核对公开和受保护内容的原权限。直接更换逻辑端点会拒绝旧引用，涉及改名的迁移应单独核对文件定位。 [S3 定位规则](s3-object-storage.md)

平台共库使用 SeaweedFS 时，停写后取得单份共库快照和同一时点的对象清单。恢复两份内容后，将两条数据库连接指向目标共库，再保留原逻辑对象位置并用目标凭据启动。在目标独立核对对象和业务引用后，通过原页面对应接口确认公开和受保护下载。[V1119 平台共库 S3 恢复](testing/current-split-recovery-v927-v928.md#v1119-当前候选平台共库-s3-恢复)。

- 变更前保存上一发行物及其摘要，备份本次影响的配置库、业务库、持久文件和远端对象存储。先在隔离目标恢复备份，并复核表数据和文件引用是否对应。
- 已有生成项目用 [受管源码更新](local-generation-update.md) 先预览再应用，保留手写 partial、AppID 和私有设置。结构变更单独核对目标与差异，按“预览、执行、核验、记录”处理。首次安装命令按本页的空库场景使用。
- 如果只有应用包发生变化，停写后核对旧包与现行数据库结构的兼容性，再恢复同一版宿主、元数据和 H5。若结构或数据已变化，按已验证的备份把数据库和文件恢复到独立目标后再切换。MySQL DDL 可隐式提交，部分成功时先保留执行回执和现场，重新反查后决定继续或恢复。恢复后复验真实登录、目标库写入、文件引用及注销。
- 本首版按新部署的 MySQL 平台和新生成应用验收。原 YZP Java 存量库及业务源码的迁移另行评估。

独立 Full 停写后，备份应用单库及 Files:RootDirectory。以目标库级账号把逻辑快照导入目标空库，按原键恢复文件，并用同版宿主和目标私有配置启动。对照客户记录和文件引用，实测授权下载、匿名公开内容和无权限拒绝。 跨 MySQL 8.4.11 实例恢复时，分别核对源和目标的服务器 UUID、独立库账号及空目标库。恢复授权与本地附件的实测见 [V1090 独立 Full 跨实例恢复](testing/current-split-recovery-v927-v928.md#v1090-当前独立-full-的-mysql-跨实例恢复)。独立应用的会话处置在其自身的身份库执行。[V1083 独立 Full 恢复](testing/current-split-recovery-v927-v928.md#v1083-当前候选独立-full-同版恢复)。

独立 Full 使用 SeaweedFS 时，还需按原对象键恢复到目标桶。保留应用配置中的存储别名、逻辑端点、区域、桶、前缀和寻址方式，目标凭据可轮换。用独立 SDK 核对目标对象，再用原接口验证匿名公开文件和带权限的受保护文件。[V1084 独立 Full 对象恢复](testing/current-split-recovery-v927-v928.md#v1084-当前候选独立-full-对象恢复)。

### 恢复快照中的会话与授权

备份会保留采集时有效的 Web 会话、Agent 访问会话及刷新授权，恢复后旧凭据仍可能有效。保持恢复目标离线并隔离入口，应用启动对外服务前，在恢复目标的身份库核对实例和库名，再处理快照中的旧会话。平台使用承载 `sys_session` 和 `sys_agent_grant` 的平台库。先读出受影响数量，再在同一连接的事务中撤销。当前候选的跨实例恢复、计数不符回滚与重复撤销见 [V1041](testing/current-split-recovery-v927-v928.md#v1041v1042-当前发行候选同版分库恢复)。 独立 Full 在自身应用库执行同一组撤销语句，同实例恢复已实测旧 Web 会话、两代 Agent 访问与刷新令牌及待批准设备码。[V1088 独立 Full 授权恢复](testing/current-split-recovery-v927-v928.md#v1088-当前独立-full-的-web-与-agent-授权恢复)。

```sql
SELECT @@hostname, @@server_uuid, DATABASE();
SELECT kind, COUNT(*) FROM sys_session
  WHERE delete_no IS NULL AND kind IN ('web', 'agent') GROUP BY kind;
SELECT status, COUNT(*) FROM sys_agent_grant
  WHERE delete_no IS NULL AND status IN ('pending', 'approved') GROUP BY status;
```

确认连接与恢复计划一致后，在同一连接执行下列事务语句。按前面的计数核对两次影响行数，完全相符后执行 `COMMIT`，否则执行 `ROLLBACK`。

```sql
START TRANSACTION;
SET @restore_utc = UTC_TIMESTAMP(6);
UPDATE sys_session SET delete_no = @restore_utc
  WHERE delete_no IS NULL AND kind IN ('web', 'agent');
SELECT ROW_COUNT() AS revoked_sessions;
UPDATE sys_agent_grant SET status = 'revoked', refresh_hash = NULL
  WHERE delete_no IS NULL AND status IN ('pending', 'approved');
SELECT ROW_COUNT() AS revoked_grants;
```

在目标隔离入口验证旧 Web 会话、Agent 访问及刷新凭据均被拒绝，再核对新登录、授权及业务访问。该步骤保留已有注销时间和 Agent 已消费刷新记录。

当前独立 Full 的原合同选择器桌面与移动 Web 见 [V948](testing/current-contract-web-package-v948.md)。平台及独立 Full 的双视口权限参考见较早的 [V913](testing/current-exact-release-web-v913.md)。实际部署域名和证书链按部署环境单独留证。

V618 从 `5c8fcb82` 验证平台发行根的 2,841 份公开源码与 49 份 Linux 宿主文件，以及独立 Full 的 49 份发布物。CLI 以该发行根作为来源两次生成 659 文件的示例 Full，逐份一致，生成宿主严格构建通过。该目录核对与 V613／V615 的实际安装和浏览器验收分列。
