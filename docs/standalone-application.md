# 独立应用包与前端宿主

独立包将项目元数据、生成强类型后端、Core/Data/Exports/Files 源码及 ASP.NET Core 宿主放入同一目录；当前 FullApplication 另含 Security/Agent/MCP 源码、数据库 Web 认证及Agent装配，BackendOnly 保留原 development-only 宿主。普通 CRUD 和文件、导出不依赖在线 Metarill 平台。MySQL 结构仍从元数据显式预览、执行和核验，普通启动不执行 DDL 或账号初始化。

## 显式打包模式

CLI 默认 `FullApplication`，包含 `frontend` HBuilderX uni-app Vue3 工程；`--backend-only` 明确选择 `BackendOnly`，保留后端与生成页面片段。完整模式遇到未接线的 CustomControl 或自定义/外部菜单时，在写 stage 前中文拒绝并指出页面或控件，不自动降级。

```powershell
dotnet run --project backend/src/Metarill.Cli -- package --metadata backend/metadata/demo.json --output .local/demo-application --namespace Example.Demo --source-root .
# 平台配置前端包含专用页面与组件，需单独提供；显式保留仅后端包：
dotnet run --project backend/src/Metarill.Cli -- package --metadata backend/metadata/platform.json --output .local/platform-backend --namespace Example.Platform --source-root . --backend-only
```

2026-10-30新增显式 `FullManagementApplication`：仅CLI/API在指定包内管理后台时，额外装配原固定管理页、权限工作台和受守卫Service。使用原业务元数据，不接受将合并后的安全管理目录伪装成业务输入。普通下载API仍按下述原规则选择，不自动切换模式。

```powershell
# 示例目录只有一个后台；实际项目必须明确选择已有后台ID：
$managementAdminTypeId = (Get-Content backend/metadata/demo.json -Raw | ConvertFrom-Json).yzAdminTypes[0].id
dotnet run --project backend/src/Metarill.Cli -- package --metadata backend/metadata/demo.json --output .local/demo-management --namespace Example.Demo --source-root . --management-admin-type $managementAdminTypeId
```

管理模式不能与 `--backend-only` 混用，输出必须是新空目录。当前固定管理模块已接原三范围受管更新及来源核验，不能切换模式或改绑后台，见[管理工程更新](testing/management-generation-update.md)；正式身份、数据库、初始化及工作台矩阵仍待完整验收。模式标识、来源声明和打包成功均不等于正式发布；具体实现和证据见[完整管理打包](testing/management-full-package.md)。

平台生成下载 API 根据服务端已发布平台项目身份显式选择 BackendOnly，业务项目选择 FullApplication。ZIP 的 `.metarill-package.json`、README 与响应头 `X-Metarill-Package-Mode`、`X-Metarill-Frontend-Included`、`X-Metarill-Package-Warnings` 均记录模式；warnings 头为 URI 编码的 JSON 字符串数组。平台行为不由客户端另传模式或路径决定。

## 独立前端边界

前端宿主保留登录表单、菜单展开/导航、请求 loading、错误提示及标准 list/detail 的交互，复用公共请求运行时、菜单状态机、FileLibrary、日期选择、搜索多选、Tiny Cloud 与 DCloud uni-ui。生成页面与页面写入 schema 只使用当前业务项目元数据。`pages.generated.json` 组装到独立宿主 `pages.json`，不复制平台配置页面；完整包仅额外复用原Agent授权/授权管理两页，普通前端片段包默认关闭该入口。

源资产来自固定逐文件 allowlist，所有祖先路径拒绝符号链接和 junction；新出现的私有文件不会自动进入包。只生成公开的项目 ID/名称/后台 ID；源平台 environment、AppID、`.local` 和 appsettings.local 不复制。依赖锁文件只允许核对后的 npm registry 和带完整性校验的离线 SheetJS 归档，许可证随包保留。元数据敏感键先脱敏，非标准键仍需发布者审核。

生成前端默认 DCloud AppID 与小程序 AppID 为空。开发者须自行配置 `frontend/common/application.config.js` 的 API 地址，安装锁定依赖，在 HBuilderX 填写自己项目的 AppID。分离端口与真机上传还需设置后端可信 `Files:LegacyPublicBaseUrl`，不从请求 Host 推断公开地址。具体操作见包内 README。

## 数据库Web认证与显式初始化

当前新 FullApplication 的认证 profile 为 `web-database-v1`，默认使用数据库账号、PBKDF2 摘要及持久化 Web 会话；缺配置、匿名或无效令牌明确拒绝，不回退开发账号。原登录、菜单、权限、注销和自助改密 URL/响应继续使用原协议；本应用后台 super 不产生平台管理员身份，Agent/设备签发只绑定该应用已发布后台，不能产生平台身份。

结构计划组合业务 `project.json`、文件三表 `files.json` 、安全六表 `security.json` 与程序集内固定Agent两表。先审核并显式执行结构计划，再从宿主私有配置或环境提供 `Authentication:Bootstrap`，单独执行 `dotnet run --project backend/Application.Host -- --initialize-security`。该命令不执行 DDL，只初始化本包项目/后台及新生成长 ID 的管理员角色/账号；重复不重置摘要、不复活墓碑或补绑既有账号。多后台必须明确选择包内后台，凭据不放入命令行、URL、源码或日志。普通启动不执行这一步。

六表是兼容平台的物理定义，不开放安全表业务 CRUD。完整安全写协调、三个手写安全 Service partial 与角色工作台下发仍待后续独立适配；共享 Security 工程随包不等于完整 Runtime A 已下发。安全表带业务页、五处平台 Custom 及其它未支持能力仍按既有门禁拒绝，不自动降级。详细配置和边界见 [独立完整包数据库Web认证](standalone-web-authentication.md)。

## 显式Development开发认证（兼容保留）

仅 Development 环境且显式启用 `Authentication:DevelopmentBootstrap` 才能使用开发账号；用户名、至少12位的密码由开发者私有配置，没有默认真实账号密码。服务端固定包内单项目，签发内存不透明会话，每次 API 验证；客户端项目选择和 query 令牌不产生身份。菜单与权限查询也受相同认证约束。

独立宿主仅在 Development 且显式启用开发认证时允许 HTTP(S) 回环 Origin 的本机跨端口预检；不允许外域或带路径、用户信息、query、fragment 的 Origin，也不设置 AllowCredentials。CORS 在认证前执行，因此真实预检可通过，匿名业务写仍返回401。关闭开发认证或 Production 不默认开放；其他Web来源需可信同源网关或自有部署策略。

本节开发账号不属于数据库 Web 账号体系，不能把开发管理员当正式身份。当前 Full 已提供上述默认 Web 模式；有自定义认证需求时仍可在手写 partial `HostCustomization.ConfigureServices` 明确适配自己的 `IRuntimeAuthorization`，并自行验证扩展合同。BackendOnly 保留本节原开发宿主，不自动获得 Web 能力。

## 重新生成与验收

包输出目录必须为空，已有手写宿主、API 配置或生成工程不覆盖。再次导出到新空目录后审查差异，保留手写文件；后端 `.g.cs` 与 `.cs` 的 partial 规则继续适用。相同输入、相同公共源资产得到相同包字节，清单记录 SHA256。

`npm test` 在 Node 24 检查 H5/Vue3 条件编译后的真实 JavaScript 语法、ES/CommonJS 导入、生成路由及组件文件闭包。真实隔离 MySQL 测试从打包结果构建宿主、显式结构同步，执行生成登录/菜单/list/detail 脚本到实际 HTTP 接口，并验证新增、显式 null、读取、删除及注销失效；这仍不是浏览器 DOM、HBuilderX 发行、小程序、Android 或 iOS 验收。Excel/DOM 第三方组件只在此脚本中检查依赖闭包，不声称已在 Node 模拟完整执行。

旧 ImageUpload/RTF URL 沿用公开上传协议，尚不具备 FileLibrary 字段的引用阻删生命周期。Agent、复杂自定义 SQL、平台设计器和完整账号/角色管理写入不在当前独立宿主已验收范围内；已接通的正式 Web 登录与初始化不解除这些门禁。

## 独立生成包 Web 与主子浏览器验收（2026-10-19）

协调者从元数据和公共源重新输出全新私有目录，装配 450 文件 FullApplication 包；导出默认 AppID 为空，只在验收副本填写用户已提供的 Metarill AppID 和回环 API 地址。`npm ci --ignore-scripts --offline`、随包 `npm test`、后端真实构建（0警告/0错误）均通过，HBuilderX 5.26 Vue3 的实际命令为：

```powershell
D:/HBuilderX/cli.exe publish web --project E:/git/metarill/.local/generated-ui-8c394058-v2/frontend --webTitle Metarill独立应用验收 --webHosting false
```

产物静态服务 5194 与独立后端 8098 分离端口，浏览器使用真实开发登录和发布菜单进入新生成页面。父记录保存前不挂载子表；保存后显示 Tab 子列表。纯界面完成父新增、PC 弹层中两条子记录新增/修改/软删除、名称筛选和重置、升序/降序回读、父备注最终保存及列表回读、父软删除和注销。子记录操作前故意修改父备注而不保存，子列表刷新后该脏字段仍保留；重新从列表进入父详情确认最终值真实持久化。父只读路由下父字段禁用，父保存/复制/删除及子新增隐藏；该前端行为不替代独立的服务端权限测试。

结构验收先证明随机子表不存在，完整预览仅一条 CREATE TABLE，持久化创建意图后显式执行并核验0差异。浏览器退出后先停本轮进程，再核对精确父ID、最终备注/金额和两条子记录的归属及软删除状态，只清理本轮随机表与父行；不存在核验及清理回执保留于私有 `.local`。两个临时开发认证配置已删除，其余既有数据未清理。

本批只验证两条子记录与 1/1 分页状态，未验证浏览器跨页边界或超过5000候选；后者已有真实 HTTP 回归，不能互相替代。小程序、Android、iOS、真实 Tiny Cloud、文件/导入导出的独立包浏览器操作和正式账号体系仍分别待验收。Tiny Cloud key按用户要求保持空值。独立8098/5194临时服务已停止。

## 跨工作副本文本规范化

受控前端文本按严格UTF-8读取、移除UTF-8 BOM并统一LF；字体和离线tgz按原字节复制。历史西语JSON已按原GB18030严格解码转存UTF-8，文案未改。真实CRLF/BOM源码副本与LF源码生成的全前端文件及清单相同，开发者改行尾后随包Node校验也可执行；未闭合条件块明确拒绝。15项前端打包回归及10项应用包/真实MySQL文件链定向回归已通过，独立克隆此前失败日志保留，不据主工作副本偶然LF把失败删除。

## 独立宿主只读结构反查

在独立包根目录运行 `dotnet run --project backend/Application.Host -- --schema-inspect`。该命令固定读取包内物理模型与部署者的私有 `Database` 配置，复用 `SchemaService.InspectSnapshotAsync`；当前 Full 合并 `project.json`、`files.json` 和 `security.json`，BackendOnly 保留原业务/文件两份模型。标准输出是单一 JSON：`executable:false`、`provider`、`databaseSchema` 和 `fingerprint`；`databaseSchema` 包含实际受管表、列、索引与 SHOW CREATE 快照，不包含业务行数据。

命令不创建执行计划、不保存元数据或执行历史、不执行 DDL，也不启动 HTTP/后台服务。普通 Web 启动不因该能力读取或修改结构。`--schema-inspect`、`--schema-preview`、`--schema-apply` 互斥，不可重复；混用、赋值变体或未知 `--schema-*` 参数在构造宿主前明确拒绝。预期库和连接实际目标不一致时拒绝，不回退到其它数据库。

这是独立包本地管理员对固定受管模型的只读命令，不是 Metarill 平台 CLI 选项目，不提供未登记表目录、元数据反向导入或任意 SQL 入口；不会把快照当可执行计划。当前仍只有 MySQL 实现，顺序快照不保证外部并发 DDL 下的一致时刻，后续变更前须重新核验指纹。

## type=2 独立选择页浏览器补验（2026-10-19）

另一全新私有包含448文件，出厂 AppID 仍空，仅在验收副本填用户已提供标识。21条随机样本通过原 CRUD API 预置，私有父页观察原 props/events，不冒充生产 MultiSelectPage 字段接线。后端编译0警告/0错误、离线依赖与闭包检查通过，v1/v2 均实际执行 HBuilderX Vue3 Web 发行。v1 首次浏览器发现 Vue computed 缓存缺陷使“选好了”持续禁用；从源修复并加真实Vue渲染回归后重新导出 v2，保留失败现场，不手改产物。

v2 浏览器在两页执行全选本页、跨页补选、仅看已选及取消不写；确认完整21个字符串ID并回读确认前 API 改名的最新服务端名称。父只读时确认与全选禁用；选择后、确认前 API 精确软删一项时整体失败，父值与确认次数保持不变。该证据与远程 List 关系分页不同，不能替代超过5000候选、双父、文件/导入导出或真机验收。

本轮随机表先留创建意图，再显式执行完整计划并核验0差异；退出登录后精确验证21项最终字段、1条软删和无外部依赖，只清理此随机表并核验不存在。8099/5195临时进程与临时认证配置已清理，原始包/截图/非凭据回执保留于私有 .local/selection-ui-aac1ce30 和其 v2目录。自有 Tiny Cloud key 按用户要求留空，未声称真实加载编辑完成。

已有独立应用无需覆盖整包；使用[本地源码更新](local-generation-update.md)的 preview/apply/recover，按三个范围显式核对差异和依赖，保留手写 partial、AppID及私有配置。该源码流程不会执行数据库结构同步、安装依赖或部署。

## 远程 List 关系包 Web 补验（2026-10-19）

452文件独立包另经HBuilderX实际发行及浏览器，甲128/乙105候选三页加载、非首屏已选名称、字面关键字搜索、关系保存和父范围切换已实际通过，详见[关系候选合同](relation-options.md)。初始样本由隔离库私有工具预置，不冒充纯界面新增；仍未验证超过5000候选浏览器、多选List或真机。浏览器注销后8100/5197服务停止，完整模型只读结构预览0差异，全部239项样本和两张随机表精确核验清理、临时认证配置删除，既有文件基础结构保持。


## 正式Web宿主与全新安装补验（2026-10-20）

源切片 `fc27127980686d3e8dfdb945e899a70ea5fe096e` 已集成。主协调者定向报告 `.local/standalone-web-authentication-integrated.trx` 为 **84/84、零跳过**（SHA256 `b93803167695df07ae636baa5da5e7dcfaffda6aabddcc544550c348036a29c4`）；随后按真实类名 `LocalGenerationUpdateTests.Cli_offline_prepare_uses_real_process_and_rejects_scope_override` 补跑 **1/1、零跳过**，单独报告 `.local/standalone-web-authentication-cli-bridge-integrated.trx` SHA256 `ea41baa9ee9836238f954d84468a8b6540b09201067f2108224338823482d2ef`。此前用文件名作类名的过滤器未匹配，不计通过，也不把首批回写为 85。上述是本机包、脚本、替身及真实 CLI 进程证据，不是 84 项实库。原更新器 64 项的 63 过/1 失败与修后单项补绿仍保留，不改成完整 64 全绿。

专属全新库批次 `1ce30f018ba84e4e` 的两个 Fact 实际 **2/2、零跳过**：独立 Full 普通启动/预览保持 0 表，显式安装 **10 表（本单业务表＋文件 3＋安全 6）**，二次无差异；正式 `Authentication:Mode=Web`、开发 Bootstrap 关闭，显式安全初始化重复不重置摘要，再完成真实 HTTP 登录、菜单/权限、上传、生成业务记录的同事务文件引用和受保护下载。平台场景单独证明平台 **23 表**、业务库 **4 表**，重复初始化及真实登录/菜单/权限；不能把这两个场景的表数混算。`result.json` SHA256 `65d3138a2c39c109dc6efe79a8f813d34b6c6741dadaec3f857a27db936cc4ee`、`fresh-install.trx` SHA256 `5cb836e32baedf60d9b48c8fac8ddfb9a0440159a08dd0e4ee12e775a0781610`，均在主仓库私有 `.local/fresh-install/1ce30f018ba84e4e/`。脚本清理后，独立只读 `cleanup-verification.json`（SHA256 `3aa97f1cd6910503e8a29271f4a6011c4328ef9a0dc9feb50abe5ec089eb5063`）确认三随机库、临时账号均为 0、所有权文件不存在；本机凭据配置亦已删除。

另一私有新目录 `.local/standalone-web-authentication-package-integrated` 真实导出 **487 文件** Full 包：包自身 `Release -warnaserror` 零警告/零错误，离线 `npm ci` 与随包 Node 闭包通过。只在该私有前端副本填写已授权 AppID，配置前后/源 manifest 摘要在 `.local/standalone-web-authentication-package-configuration.json`，不修改出厂模板。HBuilderX 5.26 Vue3 实际命令为：

```powershell
D:/HBuilderX/cli.exe publish web --project E:/git/metarill/.local/standalone-web-authentication-package-integrated/frontend --webTitle Metarill正式认证独立包验收 --webHosting false
```

2026-10-20 16:33:10 实际导出成功，产物 `frontend/unpackage/dist/build/web/index.html` 存在。最初 CLI open 未启动，随后隐藏启动 HBuilderX，重新核对 version/help 后才执行发行；原日志保留于 `.local/standalone-web-authentication-{package-integrated,packaged-host-release,hbuilder-publish-integrated}.log`，npm/Node 日志在包根。这次没有启动 HTTP 静态服务或浏览器，不把前端发行称为正式登录 DOM 验收。历史开发认证的浏览器场景仍按原冻结保留；完整 Runtime A/三个 partial、五处 Custom、Agent、正式登录/安全写浏览器、设备和性能均未据此完成。

上述本机回执汇总为 `.local/standalone-web-authentication-integrated-receipt.json`（SHA256 `398b59656f39fa683b78d938ee3ae6688907bbe79604456cc31bd73c9cca3a15`），分列 84、补 1 和 Fresh 2，记录 Web 入口 SHA256 `f33bcf8aeb2b5ae992a69c27d2cda244d3053f639a1eeba087ba8eb93df6a538` 及 46 个静态资产；新增浏览器、设备、RR、完整安全管理交付均明确为 false。


## 非冗余标量完整业务包

满足逐页固定读取、既有控件证明及显式项目/后台归属的非冗余业务，可使用默认FullApplication，无需退回BackendOnly。派生名称/联接字段只读，真实关系ID物理写入；默认值、原整表单省略/null、跨父导入及Service最终范围校验沿原协议。缺失/跨项目的表、来源页或候选页会提前拒绝，不能借打包目录或客户端projectId产生身份。

已完成包内基础项目独立构建、原Vue挂载、本地更新后重建及手写保护、专属空库12表正式安装和真实数据库Web认证/原HTTP闭环，详见[非冗余存储](nonredundant-storage.md#普通完整应用包)。专属验收脚本现在运行3项真实场景并创建4个独占随机库；每个Docker命令前检查Server-200，每轮租约绑定MySQL实例指纹，最后按成功创建的精确清单撤销账号/删除随机库并独立反查不存在。碰撞、部分创建、错实例或清理后仍有残留均不能当成清理成功；不清理旧失败样本或既有库。

这不包含新的浏览器、HBuilderX发行、真机或性能结论，也不开放尚未完成的多值、级联、全局目标、平台Custom或完整安全写协调Runtime。

本轮最终离线兼容回归54/54、零跳过，包含显式绑定未知/有效两种实际生成导入合同、候选授权、跨父依赖、完整包独立构建/原Vue及重复更新；租约工具12/12离线通过，保留原创建中断和碰撞覆盖。专属空库3/3真实MySQL、零跳过，四个本轮随机库与临时受限账号由实例绑定租约清理并独立核验，配置凭据文件移除。API离线44通过、31明确跳过；两次正式生成平台199、示例16个文件与修改前逐字节一致，前端未改。旧CI失败及本轮失败记录保留，等待新提交自身CI；浏览器、设备、性能和完整管理Runtime仍未完成。


物理FileLibrary可随普通非冗余完整包下发；Core/Files与生成Service/VO需配套更新并重建，普通query-only角色可按有效记录引用读取保护文件。参见[文件读写及权限边界](nonredundant-files.md)。这里只补齐真实引用读取，不将既有所有者路径、原V8状态映射与全部云存储策略一并视为完成。

## 完整包 Agent 接线（2026-10-28）

当前源码包已包含公共Agent、固定两表/三个唯一索引、原授权/授权管理页面及严格Web/Agent管线。`Agent:VerificationUri` 必须由部署者在私有配置填写本应用可信授权页地址；不从请求Host或客户端元数据推导。普通启动不DDL，旧工程更新须先审查新增结构计划。显式开发身份不签发Agent，无配置/匿名请求在数据库服务构造前拒绝。

固定模型派生、共享运行时、真实独立包编译及三模式HTTP、HBuilderX Web导出已有验证；正式OAuth/CLI/MySQL业务、原浏览器及独立MCP完整下发仍未验收。历史章节的无Agent下发状态以本节为准，历史实库结果仍只证明当时冻结的版本；不能把新增两表加入旧计数后冒称已经重验。详见 [Agent接线与实际证据](standalone-agent.md)。

## 公共MCP接线（2026-10-28）

完整包已包含独立Mcp公共工程、可信目录绑定及 `/api/mcp` Stateless Streamable HTTP；平台原六操作分派也复用该工程。工具保持原强类型Service与逐次页权，Agent仅允许读取，写入继续走任务保护HTTP；开发身份、缺配置或匿名均拒绝。BackendOnly不增加该正式宿主。没有新增页面、表、DDL或任意SQL入口。实际SDK协议/离线夹具、完整包及正式身份验证须分列，详见 [MCP接线与未验收边界](standalone-mcp.md)。


## 管理模块同源准备（2026-10-29，未开放普通Full发布）

[C20同源装配](security-management-application-preparation.md)已将真实三份安全partial、原权限工作台及基础工程/生产宿主/Web/Agent/MCP装入独立准备包，通过包自身严格构建、只读DI和匿名401。只有工厂真签发对象可进入内部准备路径；准备结果保留所有后端缺口，`readyForPublication=false`且没有普通Full标记。普通Create继续严格拒绝未签发安全管理声明；C06规则已确认、同库及平台跨库初始化分别接线，正式矩阵仍待。C51另提供显式完整管理模式，见上文；内部准备子集和新的打包实现均不代表C05全部交付。


## 平台业务库初始化与完整物理模型（2026-10-30）

平台整项目结构预览、显式开发初始化与独立应用使用相同文件/安全/Agent基础图，避免业务库只建立业务表却缺账号基础表。单表预览仍只处理该表。普通启动不自动改表；既有库须先预览完整差异、审查SQL，再按原同步流程执行、核验和记录，不能把新增基础表作为启动副作用。

账号初始化原按钮已接服务端登记的目标库；同物理库共享源授权事务，异库独立提交目标，仅保留平台授权读锁，不承诺分布式事务。平台项目自身、未登记连接、墓碑或归属冲突明确拒绝。普通完整包不因此增加平台管理权限或初始化Controller。有效Agent刷新授权的重置规则待用户确认，当前仍明确阻断，不对真实账号执行重置。

旧工程更新须从同一版本同步运行时并重新生成：新增MySqlDatabaseIdentity、PlatformAdminInitializationScope、PlatformAdminAccountTarget、IAdminInitializationAgentPolicy及AgentAdminInitializationPolicy纳入候选闭包校验；旧目标仍可进入原升级流程，缺依赖的新候选拒绝。AgentIntegration模板显式注册Agent策略，手写partial只首次创建。编译/离线HTTP/只读实例核验与真实初始化分别记录，见[平台初始化验收边界](testing/platform-admin-initialization.md)。


C46补齐平台自身的特殊结构来源：只有平台Program显式绑定的服务端发布目录可以保留原安全管理页参与结构组合，物理表/字段/索引仍按通用严格校验，普通独立应用的同形JSON继续拒绝。此修复不要求既有普通应用DDL或再生成，也不开放管理Full；[实际自举证据](testing/platform-self-schema-composition.md)单列。
