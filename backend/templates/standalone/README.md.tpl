# 独立应用 {{NAMESPACE}}

本目录包含元数据、强类型 Entity/DTO/VO/Mapper/Service/Controller、Core/Data/Exports/Files/Security/Agent/Mcp 源码和 ASP.NET Core 宿主。普通 CRUD 不依赖在线 Metarill/YZP。首期数据库为 MySQL，启动不会自动创建或修改表。

## 构建与结构同步

安装 global.json 指定的 .NET SDK，在包根目录执行：

```powershell
dotnet restore backend/Application.Host/Application.Host.csproj
dotnet build backend/Application.Host/Application.Host.csproj --no-restore
```

SDK 与直接 NuGet 依赖版本已固定；首次 restore 生成各项目 packages.lock.json，应提交这些锁文件，后续 CI 使用 `dotnet restore --locked-mode`，锁定传递依赖。Core/Data/Exports/Files/Security/Agent/Mcp 仅包含允许的源码及工程文件，原机器路径和私有配置不会复制。

通过环境变量设置 Database__Provider=mysql、Database__ConnectionString、Database__ExpectedDatabase，或在宿主目录自行创建已忽略的 appsettings.local.json。不要把真实凭据提交到仓库。

```powershell
dotnet run --project backend/Application.Host -- --schema-preview
# 确认变更后才执行：同一进程重新预览并执行，记录 .local/schema-history.jsonl。
dotnet run --project backend/Application.Host -- --schema-apply
dotnet run --project backend/Application.Host --urls http://127.0.0.1:5080
```

FullApplication的web-database-v1结构同步严格组合业务project.json、files.json（三张文件基础表）、security.json（六张认证/权限基础表、128列、5索引）及固定Agent模型（两表、44列、3唯一索引）。安全基础表不开放业务CRUD页面；已带安全表CRUD页的应用仍拒绝Full，不会自动降级。基础表不生成业务页面；平台自举中的相同物理定义会去重，保留表名占用、字段或索引标识冲突在打包及启动时拒绝。正常 Web 启动不执行 DDL。只对包内元数据表进行结构同步。MySQL DDL 可能已经部分生效，失败后检查记录再预览，不承诺 DDL 事务回滚。

## 认证

FullApplication默认`Authentication:Mode=Web`，使用数据库账号、PBKDF2摘要、持久化会话和本包已发布权限。普通启动不建表、不初始化账号；无数据库配置及匿名请求在打开连接前拒绝，不回退开发账户。`/health`只报告进程状态，不代表数据库或业务验收。已完成且标记PUBLIC的文件内容仍允许匿名GET/HEAD。

先审核并显式执行前述结构计划，再在宿主私有`appsettings.local.json`或环境配置`Authentication:Bootstrap:Username/Password`（至少12位，最长1024）以及可选`AdminTypeId`。多后台必须明确选择包内后台；单后台可使用唯一声明。不要把凭据传入命令行、URL、日志或仓库。随后只在包根执行：

```powershell
dotnet run --project backend/Application.Host -- --initialize-security
```

该命令只初始化本包项目/后台及新生成长ID的应用管理员角色/账号，**不执行DDL、不导入平台Config/Demo/Agent/设备数据**。重复仅核验并返回unchanged，不重置密码、复活墓碑或补绑既有用户名。并发初始化由真实事务与固定项目主键取锁/唯一冲突保护；冲突失败回滚，不自动重试。初始化需此前已安装六表，缺表直接失败。

POST `/api/admin/sys/yz_manager/login`保留URL编码表单或JSON的username/password及响应`map.loginStateId`。令牌只能在loginStateId或Bearer头传输，不接受query身份。每次请求重读会话、账号和同后台角色，停用、到期或撤权下次请求生效。仅包内真实项目/后台允许登录；本应用后台super使用独立IsRealmSuper，不产生平台管理员身份，不开放包外项目选择。Agent授权只绑定本应用已发布后台，显式开发身份不能签发。

POST `exit_login`只撤当前会话；`getMenu`/`getCurrentPermissions`沿用原菜单与动作响应。菜单及按钮按真实身份和本包固定发布目录过滤，不能由客户端项目或页面参数授权。`edit_password`保留原GET/POST合同，原页面使用POST JSON `{oldPwd,pwd}`；核验原密码后在同一事务更新PBKDF2并撤该账号旧会话，推荐POST避免URL记录秘密。部署网关也应禁止记录改密query。

可仅在Development显式使用`Authentication:Mode=Development`与`DevelopmentBootstrap:Enabled=true`和私有账号/密码；为兼容此前配置，未写Mode但显式Enabled=true仅Development也采用开发模式。显式Web不会因认证/配置错误回退；Production的Development配置仅拒绝。该开发账户仍为本机内存会话及既有私有摘要，不能当正式账号。BackendOnly继续原development-only宿主与原导出边界，不具备此正式Web能力，不能据其输出称安全Runtime已下发。

Development且显式启用开发认证时，宿主在认证前处理HTTP(S)回环Origin的CORS预检，允许本机HBuilderX不同端口携带loginStateId/JSON头，不开放AllowCredentials。来源允许不产生身份，匿名写仍为401；关闭开发认证或Production均不默认开放，其他Web来源需可信同源网关或自有部署策略。

## 业务扩展与边界

生成服务 `.g.cs` 与手写 `.cs` 使用 partial；BeforeSave 修改派生字段后必须加入 submittedFields。write-schema 列出每页允许提交的字段，服务端拒绝未知和审计字段，省略与显式 null 含义不同。表范围、权限、ID、审计和事务在服务端执行。

本包包含当前已实现的普通 CRUD、复制、JSON 行导入、查询导出投影和本地文件库。公共Agent、原授权/授权管理页面已随完整包下发；平台设计器和复杂自定义 SQL 未随包提供。Agent真实CLI与正式身份验收须由部署者执行，源码和Web构建不代表该验收通过。

文件库沿用 /api/admin/sys/file-library 协议，Files__RootDirectory 默认 .local/files，Files__MaxFileBytes 默认 100 MiB。文件目录必须持久化且不能作为静态资源公开映射；只有 PUBLIC、ready 文件可匿名访问公开流。PROTECTED 文件仅当前项目所有者或管理员可读取，pageId/recordId 不提供额外读取权限。上传计划、上传、完成是独立步骤；未完成文件不可引用。服务端校验扩展名、大小和摘要，不接收对象路径或远端地址。

生成业务记录与文件引用使用同一 DatabaseSession、同一真实事务；非空非法引用会令业务写入回滚，字段省略保持引用，显式 null 清空，删除清理引用。复杂页面 WhereSql 尚无完整范围证明时明确拒绝。不要为文件观察者另建仅连接字符串相同的会话。数据库事务不覆盖磁盘，提交结果不明或清理失败可能遗留对象，维护时应核对数据库状态，不能直接删除可能有效的文件。

标准 Excel 导出已经接通：沿用生成页的 initExport 和 `/api/admin/sys/export/export?exportId=...` 下载协议，筛选、页权限及导出列仍由生成服务确定。读取前先取得并发及票据额度，数据库按 MaxRows+1 检测超限，明确失败且不截断。原 `openUrl` 可凭此次成功导出签发的 256 位随机凭证，在两分钟内无认证头下载该文件；下载 URL 是 bearer 凭证，不应转发或记录。它在 TTL 内不会因客户端注销自动撤销，服务重启后立即失效。带有任何显式认证头时仍严格核对当前账号与项目，不降级到无头凭证；普通 `CreateAsync` 账号票据没有此授权。支持 Range，设置 private/no-store、nosniff 与 no-referrer。

Exports 配置控制私有目录、有效期及资源上限；默认目录 `.local/exports`，正式部署应提供持久存储且不能映射为静态资源。票据保存在单进程内存，15分钟到期，重启立即失效；后台分钟级清理过期和崩溃遗留文件。多实例需会话亲和或另行接入共享票据，当前不提供跨实例下载保证。可在手写 partial 中替换 IPageExportService，必须实现读取前门禁重载；不支持的实现默认拒绝且不执行准备查询。

FullApplication 模式额外提供 frontend 独立 HBuilderX uni-app Vue3 工程，包含原登录界面、正式Web/显式开发模式、服务端菜单和生成list/detail；按 frontend/README.md 配置公开 API 地址、自己的 AppID 和依赖。默认 AppID 为空，不复用平台标识。BackendOnly 模式明确不包含该宿主；backend/generated/frontend 在两种模式中都只是生成页面片段。平台专用或未知 CustomControl 在完整模式提前拒绝，不能静默替代。控件未移植项仍见 generation-capabilities.json，后端构建或 Node 检查不能替代 Web、小程序或 App 验收。

元数据已删除已知敏感配置键及密码列默认值；发布前仍需审核业务定义中是否存在以非标准名称保存的凭据。包内 .metarill-package.json 记录文件摘要；生成目录 .metarill-generation.json 保护后续生成归属。

## 原上传接口的公开地址

旧ImageUpload/富文本上传可调用 `/api/admin/sys/upload/uploadImage`，仅限真实登录的当前项目，成功文件按原协议公开。JPEG/PNG会重新编码并受10MiB、1600万像素与8192边长限制，普通附件按文件库白名单和大小上限导入。旧URL字段尚未获得FileLibrary字段的引用阻删生命周期。

`Files:LegacyPublicBaseUrl` 默认空，返回同源 `/api/...` 路径。前后端分离端口、App或真机必须配置实际可达的API公开origin；例如 `https://api.example.com/prefix` 会完整生成 `https://api.example.com/prefix/api/...`，保留可选路径前缀。该配置不含认证、query或fragment，只接受HTTP(S)；不会从Host、X-Forwarded或请求参数推断可信地址。部署者须配置对应HTTPS网关/路径转发，示例域名不代表实际可访问。

## 开发账户改密与本机私有状态

完整前端沿用原修改密码页。开发账户通过POST `/api/admin/sys/yz_manager/edit_password` 提交JSON `{oldPwd,pwd}`，必须已登录并核验旧密码；新密码为12至1024个字符。服务端保留旧GET同参数合同，推荐POST避免密码进入URL日志。成功才返回“修改成功”，并撤销全部旧会话；其他同目录开发进程下次校验同样撤销旧会话。

首次实际改密在宿主固定 `.local/development-auth/credentials.json` 创建PBKDF2-SHA256随机盐摘要（600000次），不保存明文，不接受请求指定路径。文件/目录只允许当前运行用户，原子刷盘并拒绝任何祖先/文件链接与过宽权限。摘要绑定项目和显式配置用户名；重启保留新密码，单改原启动Password配置不能覆盖已持久化摘要。状态损坏、已存在目录缺文件或身份不符均拒绝，不回退复活原密码。需要重置时由开发者停止宿主、保留并核查私有状态后实施自己的明确重置流程，本包不提供远程重置后门。

该私有目录不进源码/包清单，也不会由运行时同步写入或恢复。真实使用时随部署者私有配置备份且限制访问，不能公开映射/提交Git；本项目用户约定的重要凭据备份应按项目归档并附说明。本节私有摘要只属于显式Development开发账户；正式Web凭据独立保存于数据库。源码与离线测试不代替正式数据库、部署、完整安全CRUD或设备验收。

## 生成类型的手写扩展

Entity、页级DTO/VO、Mapper、Service、Controller及导入请求均支持同程序集`partial`。生成的`.g.cs`继续归元数据所有；在同一命名空间另建`.Extension.cs`编写方法，重复生成与本地页面更新保留这些非归属文件。Service原手写壳仍只首次创建，继续实现`BeforeSave`、`BeforeDelete`；不再生成空转Business。

页级DTO/VO、Mapper、Controller保留`sealed`，`partial`只拆分同一个类型；手写文件不要重复生成属性、基类或主构造函数。Mapper/Controller的主构造函数仍仅由生成文件声明，其他分部可访问其参数及生成的private成员。Controller辅助方法加`[NonAction]`，公开业务动作仍需明确路由和权限；DTO/VO扩展属性会影响JSON协议，辅助值按需使用`[JsonIgnore]`，实体非持久化属性需明确`[Column(IsIgnore=true)]`。不通过手改生成属性调整数据库或标准接口。

已生成工程跟进时更新生成器、重新生成并编译；无需SQL迁移或元数据回填。公开完整包的其他能力门槛不因partial声明解除。

## Agent 配置与升级

完整包在同一应用数据库安装 `sys_agent_grant`、`sys_agent_task`。旧工程更新后须显式预览并审核新增表，再应用结构计划；正常启动不DDL。`Agent:VerificationUri` 默认空，须在私有配置填写本应用原授权页可信HTTPS地址，例如 `https://app.example/#/pages/custom/agent_authorize`；本机验收允许HTTP回环。服务端不会从Host头或客户端参数推导该地址。

OAuth和 `X-Yzp-Agent-*` 协议沿用现有CLI；批准需要当前后台真实Web超级角色。所有业务请求继续核验当前账号及页面/按钮权限，写请求必须逐次确认并带UUID；重复UUID拒绝执行，应查任务并回读。账本与业务不构成跨库分布式事务，响应或记账失败返回unknown，禁止自动重发。开发身份和缺数据库配置均不开放Agent接口。

## MCP

完整包提供同路径 `/api/mcp` 的Stateless Streamable HTTP，使用本应用同一真实Web/Agent认证。`list_generated_pages`只列当前允许的固定页面/动作；`call_generated_page`保留list/detail/create/update/delete/copy、parent及强DTO合同，写入必须confirmed=true。服务类型和程序集由宿主固定，不接受客户端类型名或任意SQL。对象响应中的长整数保持字符串。

Agent仅可经MCP读取本授权项目页面；即使confirmed=true，也不能通过MCP写入，应使用原带UUID及任务账本的HTTP能力入口。显式Development身份不发布MCP，无数据库配置/匿名请求在工具执行前拒绝。该模块不开放平台设计器或元数据编辑。普通启动不DDL，新增模块无需新增数据库表。

旧完整工程须通过原运行时更新预览/应用，配套同步Mcp源码、中央NuGet依赖及宿主引用、McpIntegration与Program，不改原页面或手写partial。源码、离线SDK测试和包编译不代表部署后的正式身份/数据库/文件关系/并发/设备或性能验收通过。
