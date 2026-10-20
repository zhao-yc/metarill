# 实施与验收台账

来源：YZP 工作区 `D:\\svn\\yzp`，参考提交 `72c46ca6`。验收更新：2026-10-20（前序记录保留原日期）。独立目录 `E:\\git\\metarill`，公开仓库 [zhao-yc/metarill](https://github.com/zhao-yc/metarill)，开发分支 `codex/initial-platform`。旧 Java 工作区未修改、未部署。

## 当前结论

首期 C# 基础与 MySQL 核心纵向闭环可开发、可审查；**完整 YZP 能力、前端跨端及性能验收尚未完成，不作为正式版本发布**。普通数据访问采用 FreeSql；配置、权限、文件等基础设施以同一 DatabaseSession 的参数化命令加入事务。

当前原页面、样式、交互及模板恢复的证据以 [前端恢复与原协议台账](frontend-preservation-correction.md) 为准；下文前序简化页面记录不计为原界面验收。角色规则已按用户确认沿用原V8页面操作权限及目标后台归属，不增加超级身份门槛，见 [角色管理规则](role-assignment-policy-decision.md)。原角色复选读取及同后台工作台入口已恢复，标准安全表写协调已接线且完整实库回归通过，独立实例三个RR场景已验；原账号/角色名称、掩码和启用只读补验，后续凭据HTTP/MySQL三项已验；账号浏览器完整写入及独立宿主安全闭环仍待验，不能以早期双重平台管理员门禁作为最终实现。

| 能力 | 已取得证据 | 剩余边界 |
| --- | --- | --- |
| 仓库、许可证与环境 | 独立历史、公开仓库、Apache-2.0；官方 .NET 10.0.402 SDK 校验并安装 | CI 结果另以实际 Actions 为准 |
| 平台自举 | 23 张表、649 列、20 页、665 个页字段；固定标识清单；重复初始化不复活逻辑删除记录 | 首版不迁移旧业务数据与代码 |
| MySQL 核心流程 | 真实登录→配置表/批量字段→结构预览/执行/二次核验→生成 ZIP→业务 CRUD | 浏览器已验临时表保存、字段配置、结构执行和生成下载；原选择器许可已确认，从零纯界面选择后台、建表及默认字段回读已通过 |
| 生成与独立应用 | 强类型 DTO/VO/Mapper/Service/Controller；严格字段存在性；受保护 partial；独立应用包真实编译及 MySQL CRUD/复制/事务；FullApplication 已组装独立 uni-app 宿主，平台显式 BackendOnly | 共享导出、本地文件与同库引用事务已验；开发登录/菜单/生成 CRUD 的真实 HTTP 脚本通过；独立包已实测 Web 发行、主子 Tab/PC 弹层和独立多选页浏览器；独立宿主正式认证/完整安全协调及Agent未完成；文件、复杂关系导入导出、下载落盘和真机仍缺；原单表导入及服务端XLSX已有实证 |
| 菜单、权限 | 单表树、页面归属后台；项目行锁/版本冲突；导航与页操作分权；真实身份后台读取；凭据HTTP/MySQL三项 | 原角色复选读取及同后台工作台入口已恢复；平台标准安全表协调已接线，独立实例RR三场景已验；账号原页浏览器完整写入和独立宿主下发仍缺，不把菜单可见当写授权，不以额外平台超级门槛替代原规则 |
| 配置工作台 | 页面布局、字段批量开关/排序、批量建字段、索引聚合、页面复制/创建、快捷建菜单 | 保存配置不会自动执行 DDL；高级联接、物理视图等尚未移植 |
| 结构同步与只读反查 | 指纹/短期计划、防漂移/重放、MySQL 锁、结果审计；部分 DDL 失败明确记录；HTTP/MCP 受管结构反查固定 executable:false | 不自动写元数据或执行反查 SQL；独立宿主已提供固定受管模型 --schema-inspect；平台 CLI 选项目、未登记表目录及反向导入仍待完成；不删除未声明结构，不支持索引自动替换/删除、DESC，破坏性变更拒绝 |
| 文件 | 真实上传/完成/解析/访问、目录操作；业务引用同事务；删除与引用并发保护 | 当前本地存储；平台/已发布示例业务库接线与同库引用事务已通过；原RTF/ImageUpload URL尚不追踪引用生命周期，S3待实现 |
| 导入导出 | 前端行数据导入整批回滚；真实 XLSX；身份绑定短期下载；读前并发/票据门禁与 SQL 上限 | 复杂控件/关系按能力清单明确拒绝；不静默截断 |
| MCP | 官方 C# SDK 2.2.0；匿名拒绝；工具列表、结构预览和同 Service 页操作通过真实客户端 | Agent 的 MCP 写操作当前拒绝，写入走带任务保护的能力 API |
| Agent | 设备码授权、刷新轮换/撤销、能力目录、写确认、持久任务 UUID 去重；实际 CLI 端到端通过 | 人工确认由隔离测试账号模拟协议，尚无真实浏览器授权操作验收 |
| 前端 | 新AppID与HBuilderX Web发行；原登录/导航/单表CRUD/导入、配置表及选择器建表；原账号角色只读补验；最近完整前端260项 | 其余业务及账号浏览器完整写入、小程序/Android/iOS真机待验收；Tiny Cloud 按用户决定空key，真实加载/编辑待配置；本机 HBuilderX 预览入口未启动，使用发行产物静态服务验证 |
| 性能 | 保留固定类型热点查询/映射；导出实测 LIMIT 上限和满额不读库 | 未完成与 Java YZP 同数据/SQL/索引/并发/资源的对照，不作性能达标结论 |
| 多数据库 | Provider、逻辑类型、能力、结构计划与平台/业务连接边界 | 目前只实现 MySQL；MariaDB/PostgreSQL/SQL Server/Oracle/SQLite 尚未支持 |

## 验证方式

协调者在 Server-200 的 `metarill_test` 与 `metarill_business_test` 两个独立库运行全部后端测试，凭据不提交；TRX 保留于私有 `.local/acceptance-results`。前端运行时与 CLI 测试使用 Node.js，真实 CLI 另以测试后端验证设备授权、目录、预览不写入、确认新增、任务查询、重复 UUID 拦截、逻辑删除及撤权。

CI 在 Linux / Windows 重新生成、比对提交产物、编译宿主并遍历所有测试项目。CI 不持有私有 Server-200 凭据，无隔离配置的 MySQL 用例明确跳过；绿色 CI 不等价于数据库或设备验收。

`--synchronize-development-seed` 仅用于本次新建开发/测试库的种子标识校正，限定库名并先备份包含逻辑删除的元数据快照。它会替换内置平台与示例元数据，**不能用于已有用户项目或普通初始化**。备份已独立保留，不包含原 YZP 业务数据。

## 待验收顺序

1. 原单表新增/修改/复制/软删及合法/坏批次导入已补验，继续账号浏览器完整写入和其余页面；原选择器许可与接入、新AppID、Web发行均已通过。自有 Tiny Cloud 保持空key，真实编辑按用户要求留待后续。
2. 主子 Tab/PC 弹层与独立多选页已完成独立包 Web/浏览器核心验收；继续List关系大数据、文件、复杂关系导入导出及下载落盘；补齐高级联接、独立宿主生产认证/安全协调/Agent和已接在线助手的原按钮整链。
3. 完成 Web、小程序、Android、iOS 真实验收和 Server-200 同资源性能对照，稳定退化阻断发布。
4. MySQL 首版完整验收后，再依次适配其他数据库；每种分别验证自举、反查/DDL、生成/CRUD、权限/关系、导入导出和事务并发。

## 本次集成验证记录

2026-10-18，在 Server-200 独立测试库执行：Agent 16、API 9、配置动作 29、Data 36、Exports 16、Files 17、Generation 15、Metadata 49，共 **187 项通过，0 失败、0 跳过**。前端运行时 **188 项**、CLI 协议 **19 项**通过；实际 CLI 流程另验收一次。重复生成产物字节一致，24 个手写 partial 文件摘要保持一致。

[Linux / Windows 与前端 CI 记录](https://github.com/zhao-yc/metarill/actions/runs/39121606269) 三项作业均通过。CI 缺少真实 MySQL 凭据的数据库用例按设计跳过；真实数据库证据来自上述独立测试库。

索引工作台保留从配置表详情携带 `config_table_id` 进入，不能新增无表上下文的全局菜单。开发服务通过 `scripts/dev.ps1 start` 启动后已实测健康检查、登录、当前项目菜单及注销；日志和停止入口见 README。以上结果不替代尚未完成的发布验收。

## AppID接入后的Web验证

用户提供并授权使用 __UNI__CF80CAE，已备份原manifest并写入回读。HBuilderX 5.26.2026091802（Vue3）实际发行命令成功，产物位于 frontend/metarill_uni_admin/unpackage/dist/build/web；首页及3项入口资源HTTP 200。关闭默认统计注入后的首页SHA-256为0E87A59D53CDEEB7B825B518119FCBDC7AE001FDA21BD37F08F61486950D955F。

真实内嵌浏览器已登录、选择Metarill项目，读取菜单、23项配置表列表及配置表详情/从属页字段。发现并修正运行时菜单text/icon/projectId/adminTypeId响应遗漏，补齐原存储模式控件只读能力接口；supportsNonRedundant明确为false，不把尚未实现的能力标记为支持。新增2项真实HTTP回归后，API测试11/11通过；本轮前端188/188通过。

该验证没有执行元数据保存或DDL；选择器与富文本继续显示许可待确认状态，不能据此宣布完整前端或多端验收通过。

真实浏览器的一键生成已捕获下载事件，取得metarill-application.zip（428543字节、226条目），核验含backend/Application.Host/Program.cs与src/Metarill.Data/Metarill.Data.csproj；SHA-256：A8906995DB1A4FADC9DD23F4CE5B0BCA1C3B920C05884E5D34F3CCC5A73C2B76。它是生成包下载验收，不表示包内完整前端/运行时已经全部移植。

浏览器注销后刷新仍显示登录页，会话已清理。临时静态服务仅供本机查看发行产物；日志为.local/h5-preview.log，停止入口为.local/stop-h5-preview.ps1。本轮未上传或部署站点。

## 独立应用导出切片（2026-10-19）

纯导出源码从 Runtime 抽取为 `Metarill.Exports`，保留原 `Metarill.Runtime.Exports` 命名空间；五个实现文件内容未改变。平台 Runtime 与独立包共同引用该工程，包内只带 Core、Data、Exports 基础源码，不引入平台元数据、Agent 或文件运行时。生成宿主注册同一 Excel 服务与过期清理任务，并保留 `/api/admin/sys/export/export?exportId=...` 下载协议。

本轮导出测试 **16/16** 通过，覆盖真实 XLSX、长编号文本、公式文本、账号/项目绑定、确定性时钟过期、容量与并发门禁、取消和文件清理。API 构建 **0 警告、0 错误**。生成测试 **15/15** 通过，其中真实独立包以自身源码编译，使用随机回环端口启动临时 HTTP 宿主，验证登录、固定投影导出、XLSX 解包、Range、私有缓存头、匿名及注销后拒绝、满额拒绝；测试结束终止进程并移除临时包。测试投影入口仅写入临时测试包，不属于产品输出。

HTTP 验证不连接数据库，未经过真实业务库的生成页 `initExport`、筛选/数据范围和关系名称补全；这些路径不能因固定投影下载通过而宣称独立包业务验收完成。过期、跨账号/项目拒绝由共享服务测试覆盖，本轮未补做这些场景的独立包 HTTP 验收。票据限单进程内存，重启失效；多实例共享票据、独立包前端、文件和 Agent 仍待实现或验收。本轮没有部署，也未启动日常 8088/5188 服务。

## 独立应用真实业务库导出补验（2026-10-19）

新增可选 `StandaloneExportMySqlTests`，仅显式提供 `METARILL_TEST_SETTINGS` 且 BusinessDatabase 为 MySQL、ExpectedDatabase 严格等于 `metarill_business_test` 时执行；连接串目标库和实际连接身份另由 DatabaseSession/SchemaService 校验。未提供配置时明确跳过，不计作数据库验收成功。

本轮在 Server-200 隔离业务库实际通过 **1/1**：复制 demo 元数据，仅将表名改为唯一随机 `mt_test_export_` 表，保留项目、表、页面和字段标识；SchemaService 预览/执行建表后二次预览无差异。独立包离开原仓库引用完成真实 restore/build，随机回环端口启动包内宿主，私有连接只经进程环境传入，开发不透明会话登录后通过真实生成页 add 新增两条不同名称记录。initExport 按 name 筛选得到一行，下载解包核验中文表头、排除另一行以及 demo 列表的19位项目编号完整文本类型。MaxRows=1 下全量两条请求明确拒绝，目录文件集合保持不变，无部分文件；匿名下载及注销后下载均为401。

成功与失败均进入 finally：先终止临时宿主，再仅删除本测试确认原先不存在的唯一随机表，并删除临时包目录。未触碰原 demo_customer、开发库、Docker 或8088/5188服务。该记录补齐上一节尚未执行的真实数据库生成页导出边界；复杂关系名称补全、独立包前端、多实例票据和生产部署仍未验收。

包含该数据库用例的生成测试全量 **16/16** 通过、0跳过；移除显式测试配置后，该用例实际报告 **1项跳过**。

## 本轮集成回归与浏览器写入记录（2026-10-19）

1. 从生成源补齐 ConstList/ConstDownList 的 `xxxShow` VO。固定类型 switch，不在每次请求解析常量元数据；未知值保留原文，null 不变，原字段与展示字段分离。覆盖字符串转义、重复小数、大整数、只读别名冲突及真实 HTTP 拒绝写回。
2. 在独立示例开发项目准备唯一临时表后，浏览器完成名称保存并刷新回读、两条字段预检/批量保存、原创建表按钮跳转、完整计划确认执行、再读取数据库剩余 **0** 差异、生成 ZIP 下载和删除。该入口准备使用了 HTTP，因为后台类型选择器许可仍待确认，不能宣称纯界面建表完成。
3. 浏览器发现原 DELETE 完整实体与严格 ID DTO 不兼容，已在公共请求边界按登记路由转换；后端未放宽绑定。真实界面重验删除成功，随后只清理本次空的随机物理表，既有平台配置与原示例业务记录未改。
4. 生成全量中复现临时 DLL 短暂占用导致收尾失败；确认随后可独占打开，新增只限随机测试目录的绝对路径校验和最多3秒等待，持续占用仍使测试失败。完整生成回归及该真实宿主测试连续三次复验通过，不使用跳过掩盖问题。

最终集成：Agent **17**、API **12**、配置动作 **29**、Data **36**、Exports **16**、Files **17**、Generation **17**、Metadata **49**，合计后端 **193 项通过，0 失败、0 跳过**。前端 **195/195**，CLI **19/19**；私有 TRX 位于 `.local/acceptance-results/20261019`。最初漏设平台元数据环境变量导致的数据库跳过已补配后重新执行，未计作通过。连续两次实际再生成字节一致，24 个手写 partial 的 SHA-256 均保持不变。

实际 HBuilderX 命令：`D:/HBuilderX/cli.exe publish web --project E:/git/metarill/frontend/metarill_uni_admin --webTitle Metarill --webHosting false`，日志 `.local/hbuilderx-web-build-20261019.log`，已确认编译/导出成功。当前预览 http://127.0.0.1:5188/；临时预览日志 `.local/h5-preview.log`，停止 `.local/stop-h5-preview.ps1`。后端 http://127.0.0.1:8088/api，日志/停止使用 `scripts/dev.ps1 logs` 与 `scripts/dev.ps1 stop`。均未生产部署。

## 独立应用共享文件库（2026-10-19）

文件服务、引用观察器及原 HTTP Controller 提取到 `Metarill.Files`，基础设施行存储移入 `Metarill.Data`；原命名空间和文件接口保留。平台与独立宿主复用相同实现，发布时固定业务元数据快照。独立包不携带平台注册器、Agent 或平台配置库凭据，文件基础表不作为普通业务页面暴露。

`backend/metadata/files.json` 由平台模型派生，含3张表、49字段、5索引和15索引字段。执行 `node tools/metadata/derive-files.mjs` 更新，CI使用 `--check` 阻断漂移。结构模型合并不修改输入；平台自举的相同物理定义去重，保留表名占用、表字段/索引不一致、跨类标识冲突或普通页面开放基础表均拒绝。包内正常启动不建表，`--schema-preview` / `--schema-apply` 才显式处理业务表与文件基础表。

真实独立文件验收使用 Server-200 的 `metarill_business_test`：从包自身源码 restore/build，随机回环端口启动，普通启动及预览均不创建随机业务表，包内 apply 后再次预览无差异。通过 HTTP 上传计划→multipart→完成、SHA-256与服务端MIME核对、公开/受保护GET/HEAD/Range，以及生成页新增和修改同步引用；省略保持、null清空、引用阻删，未完成/跨项目/访问级别不符/未知文件令业务字段和引用整体回滚。删除业务记录先解绑，然后可删除文件；注销后原会话的列表、上传计划和受保护内容均401，公开ready内容仍可匿名读取。开发认证只有单项目管理员，跨 owner 非管理员边界仍由共享服务的原有测试覆盖，不能把该测试写成多用户 HTTP 验收。

新增文件模块回归验证发布快照、重复归属拒绝、SecurityException拒绝新增后合法修改上传回退、HTTP可达的100MiB服务端上限。两条独立数据库连接在仅登记三张文件表的存储上交叉移动目录：第一条持锁，第二条等待后拒绝成环，无平台项目表依赖。目录移动锁定当前项目全部有效目录，尚未进行大量目录下的吞吐对照，不作性能达标结论。

最终复跑已确认三张文件基础表在同步前均存在且无结构漂移，仅验证复用并保留；首轮未记录它们的创建前状态，不能据此断言全新库的三表首次安装已取得实证。验收只清理捕获的文件/记录ID、确认原先不存在的随机业务表与专属临时目录。磁盘操作不具备数据库事务的原子性，物理删除失败仍明确返回。未改前端交互、未重新做本轮H5/真机验收、未生产部署；原组件许可继续待确认。

本轮主协调者执行：Agent17、API12、配置动作29、Data36、Exports16、Files21、Metadata49，生成原有与新增组合27项加独立文件真实用例1项，累计后端 **208项通过、0失败、0跳过**。生成项目整体27项后仅新增测试文件，再单独复验该用例；不将并行代理结果冒充主协调者执行。构建0警告/0错误，文件派生模型`--check`通过。前端产品文件未改，本轮未重跑前端单测或HBuilderX发行；上一节195/19及Web记录保留为前序证据。

开发后端已通过 `pwsh -NoProfile -File scripts/dev.ps1 start` 恢复，`http://127.0.0.1:8088/api/runtime/health/check` 业务码200，既有 `http://127.0.0.1:5188/` 静态预览HTTP200。后端日志/停止使用 `scripts/dev.ps1 logs` / `scripts/dev.ps1 stop`，预览日志与停止入口沿用上一节。

## 全新空库与文件接线集成验收（2026-10-19）

协调者在最新集成版本运行 `scripts/acceptance/fresh-mysql.ps1`，编号 `000ffb6eb3134299`。Server-200 身份与地址在每次 Docker 调用前核验；平台与独立应用两个首次安装场景 **2/2 通过、0 跳过**，普通启动不建表、显式安装及重复初始化保持数据均实际验证。3 个独占随机库及 1 个最小临时账号已全部清理，临时凭据文件已删除，回执保留于私有 `.local/fresh-install`。详细方法见 [首次安装验收](fresh-install-acceptance.md)。

平台文件定位按已发布项目解析，平台/业务库分别使用正确的会话、目录与事务；公开地址的 projectId 只是定位提示，仍验证实际文件归属与 PUBLIC 状态。HTTP 与 MCP 完整结构计划使用相同文件基础模型。协调者真实文件库/旧上传集成回归 **38/38 通过、0 跳过**；JPEG/PNG 使用实际 native 解码重编码，未知格式、超限、假扩展名和伪 Host 均拒绝或不采信。Linux native 的结果以本次后续 Actions 为准，不能用 Windows 结果替代。

索引聚合测试曾实际发生死锁；补齐元数据的父键与项目索引后，两个独立项目的聚合锁互不阻塞，真实 Metadata **56/56 通过**。生成配置写入另增加事务准备守卫，统一先锁项目、再锁记录；默认系统字段补齐必填布局及稳定排序，真实 API 新建并回写回归通过。当前测试计数以各切片和后续完整集成统计区分，不叠加重复运行充当新用例。

## 本轮协调者完整组合回归（2026-10-19）

当前已提交组合按8个后端项目逐个执行：Agent17、API27、配置兼容29、Data36、Exports16、Files38、Generation39、Metadata65，合计 **267项通过、0失败、0跳过**。首次安装两项使用独占空库单独实际执行并通过，不混入普通配置下的跳过用例；合计实际数据库/后端场景 **269项**。Release构建0警告/0错误；前端217项、Agent CLI19项、空库清理保护6项通过，npm audit全级别0项。

HBuilderX Web再次实际发行成功；重复生成215个文件字节一致，24个手写partial未变化。本轮推送后仍需核对Linux/Windows/前端Actions，尤其Skia的Linux native执行。开发服务在回归前已停止以解除Windows DLL占用；此临时构建锁问题未记为产品测试通过，也没有隐藏此前实际索引死锁。完整独立前端、主子关系、关系分页及其他主框架缺口继续按审计推进。

## 主子、完整独立包与关系 UI 状态补记（2026-10-19）

以下为 `a1c2ed0` 已集成源码及后续私有包准备的状态补记，不覆盖或累加前述历史测试批次。`ConfigChildren` 已由专用编译器消费，校验同后台、父范围/外键和循环，生成父详情 Tab、独立打开及 PC 弹层，并传递父只读上下文。页面权限未就绪、父记录未保存/未回读时禁写；生成 Service 继续按真实身份校验父记录和项目，前端显示不替代授权。

FullApplication 已提供受控独立 uni-app 宿主；平台导出与 CLI 显式 BackendOnly 在清单、README 和响应中标明不含完整前端。开发认证、回环 CORS、真实 HTTP 脚本 CRUD 与默认空 AppID/开发者配置后闭包检查已有回归。协调者私有 450 文件主子 v2 包通过 npm 闭包检查和后端编译，本轮独占随机子表显式安装后重新核验 0 差异；创建前意图与结果由私有回执保存。该准备不计为 Web 发行、浏览器或真机验收。

关系 UI 子任务实际 Node 回归 229/229，生成器 66 项通过、2 项专用全新库测试未启用而跳过；这些是子任务批次，不能写成协调者完整组合结果。新 List 选择器走关键字分页和每批最多 200 ID 的已选名称回读，保留单选 null、零和长 ID；同 ID 跨不同目标父组不复用另一组成功标签，双父缓存/标签串用 P2 已修复。未知目标父保留原 ID 与只读提示，完整独立包资源清单和实际页面仍须协调者最终核验。级联、非冗余读/传播、维护回填及其它未支持控件继续按能力缺口处理。

HTTP/MCP 受管结构反查已具备真实管理员/项目/表归属校验，返回 executable:false；不自动保存元数据或执行 DDL。CLI、未登记表目录和确认后的反向导入仍待交付。当前独立包 Web/浏览器待主协调者实测；正式生产认证、Agent 宿主、小程序/Android/iOS、其它数据库及同条件性能对照均未因此完成。

## 独立主子包真实 Web 与浏览器补验（2026-10-19）

后续协调者集成远程关系选择器的三项受控资产，从新空目录生成450文件独立FullApplication包。随包Node闭包检查、后端构建和HBuilderX 5.26 Vue3实际Web发行通过。真实浏览器从独立5194站点登录8098后端，经发布菜单执行父新增、子Tab与PC弹层新增/修改/删除、筛选/重置及名称升降序；子操作保留父表单未保存备注，再保存父并从列表读到最终备注和123.45金额。父只读上下文隐藏父子写入口，父软删除与注销回到登录页也已实际验证。详情见[独立应用包](standalone-application.md)。

本轮使用200服务器隔离业务测试库。随机子表创建前不存在，完整结构计划仅一条该表CREATE TABLE，显式执行后0差异；清理前实际核验精确父ID、最终字段和两条子行全部软删除，只移除本轮随机表及捕获的父行，未清理其它既有记录。临时服务已停、临时认证配置已删，意图/安装/清理回执和原始日志保留于私有`.local/generated-ui-8c394058-v2/.local`。

协调者合并后Generation **81/81**、前端 **229/229** 通过，零跳过；平台工程也再次由HBuilderX发行Web成功。这里没有将此前子任务66+2skip数字累加。其余后端与专用全新空库的最新组合结果另行记录。两条子记录的浏览器测试不能代表跨页边界、关系大数据选择、文件/导入导出或真机验收；正式认证、Agent、其它数据库及同条件性能对照保持未完成。

## 主子与关系集成后的完整组合回归（2026-10-19）

协调者在统一再生成后的当前组合逐个执行8个后端项目：Agent17、API30、配置兼容29、Data36、Exports16、Files38、Generation81、Metadata68，共 **315项通过、0失败、0跳过**。Generation普通组合显式排除专用全新空库类；随后运行Server-200专用脚本，编号 `80a569a82d214bd1`，两项首次安装真实 **2/2通过、0跳过**，3个随机库与1个临时账号全部清理、凭据文件删除。合计 **317项后端/数据库场景实际通过**，不叠加前述重复运行或代理批次。

当前前端229/229零跳过；平台与450文件独立包均实际HBuilderX发行Web成功。Release使用CI同一实际目标 `dotnet build backend/src/Metarill.Api --configuration Release`，0警告/0错误；此前误用不存在的slnx路径仅是命令路径错误，原失败日志保留，未计为构建通过。两次189项受管产物及2份归属清单字节一致，24个手写partial不变。新提交的Actions仍需单独核对，不能借用d42e360旧批次三绿结论。

## type=2 多选页、本地更新与只读命令补验（2026-10-19）

协调者从当前源导出448文件 FullApplication 私有包。v1 首次浏览器验收复现确认按钮永久禁用：computed 初次读取非响应式 _alive 短路，未订阅 selectionReady。直接调用 getter 的旧探针没有覆盖 Vue 缓存；新增真实 Vue3 Options API/createRenderer 两项测试，修复前均复现、修复后均通过。未手改失败包，重新导出 v2、重新执行 HBuilderX 5.26 Vue3 Web 发行后再验。

21条样本由原 CRUD API 预置而非浏览器创建；私有父页只观察原 selectedIds/selectedNames/nameField 和 onPopSelect(row[])/onPopClose。真实浏览器验证两页跨页草稿、全选本页、仅看已选、取消不改父值；v2 确认收到21个完整字符串ID，确认前经 API 改名的一行回读为服务端最新名称。父只读时确认与全选均禁用；之后选择已完成、确认前经 API 精确软删一项，整体回读失败并显示中文错误，父确认次数和值保持不变，没有接受20项合法子集。API竞争变更与浏览器操作明确区分，不算完整生产Multi字段接线、双父或所有关系控件验收。

随机表 mt_ui_sel_aac1ce30 原先不存在，显式安装后0差异；结束后核验21个精确ID、最终字段和恰1条软删、无外部依赖，才删除该表并核验不存在。8099/5195服务停止、临时认证配置删除；v1/v2失败/成功包、意图/安装/更新/失效/清理回执与截图保留在私有 .local。独立宿主 --schema-inspect 实际返回4张受管表及指纹，executable:false，元数据字节保持。

本地源码更新 CLI 的三范围预览/应用/恢复已由协调者20项完整定向通过；候选菜单/未知只读控件能力校验另2项实际通过。错误 filter 没有匹配测试的初次运行虽退出0，未计为通过。跨工作副本CRLF/BOM与原西语编码导致的失败已保留，从源修正后完整打包定向复验通过。前端当前231/231、0跳过；平台工程本轮 HBuilderX 发行成功。最新后端/全新空库总数在后续最终组合记录，不将重复定向运行累加。

此前6b005e37的 [Actions](https://github.com/zhao-yc/metarill/actions/runs/39207044014) Linux、Windows、前端三项均已实际核对成功；新提交仍须独立核对其 run，不能借旧CI声称新代码通过。

## 本地更新与多选集成后的最终组合回归（2026-10-19）

协调者在统一再生成后的组合逐个执行8个后端项目：Agent17、API30、配置兼容29、Data36、Exports16、Files38、Generation108、Metadata68，共 **342项通过、0失败、0跳过**。原始TRX与摘要保存于私有 `.local/acceptance-results/final-updater-selection-20261019`。普通组合显式排除专用全新库类，随后以Server-200脚本运行编号 `ecff0bdbc06a4c27`：两个首次安装真实 **2/2通过、0跳过**，3个随机库与1个临时账号全部清理、凭据文件删除。此批合计 **344项后端/数据库场景实际通过**，不叠加前述定向或代理批次。

前端231/231、0跳过；平台HBuilderX实际Web发行成功，命令仍为 `D:/HBuilderX/cli.exe publish web --project E:/git/metarill/frontend/metarill_uni_admin --webTitle Metarill --webHosting false`。独立type=2 v2包另已实际发行并完成上述浏览器复验。当前模板两次189项受管产物及2份归属清单字节一致，24个手写partial不变。HBuilderX只读枚举Android与iOS均退出0但未返回设备明细，不能据此填写设备安装或真机通过；三类真机证据仍空缺。Tiny Cloud按用户要求保持空key。

本轮源码更新CLI不等于平台三个原动作/自动助手通道已经接通，独立多选页不等于完整Multi字段、非冗余读计划或名称传播完成。正式认证、Agent宿主、独立文件/导入导出及远程List大数据浏览器、反向导入、真机、同资源Java性能与后续多库仍按主框架审计继续推进，不作完整替代或发布结论。

当前组合的 Release 实际构建也已通过：dotnet build backend/src/Metarill.Api --configuration Release，0警告、0错误。开发API在回归期间保持停止以避免Windows DLL占用；不影响上述临时独立包和清理证据。

## 远程 List 双父范围与分页浏览器补验（2026-10-19）

主线fbb5cbd的[Actions](https://github.com/zhao-yc/metarill/actions/runs/39214834883)已核对Linux、Windows、前端三项均成功；CI仍不持私有MySQL凭据。本次新452文件独立关系包经过实际依赖闭包、后端构建和HBuilderX Web发行；中断造成的零字节SDK缓存及后续构建错误保留，明确只修临时缓存，未改产物源码。

真实浏览器验收甲128/乙105候选三页、第三页初始名称、特殊字符字面搜索与注入形态/越权反例无结果、合法替换保存/列表与详情回读、目标父切换的旧名称失效/完整ID保留和恢复、乙记录独立范围及注销。全部239样本由私有隔离库事务预置，非纯界面创建；详见[关系候选合同](relation-options.md)。来源长ID与关系值均超过JS安全整数范围且完整回读。本批不冒充超过5000候选、多选或跨端设备验收。

临时8100/5197已停止、认证配置已删除；完整包最终结构预览0差异。两张随机表mt_rb_d5a7e23479_source/target在完整239项内容、精确最终关系、结构/触发器/外部依赖校验后已清理并核验不存在，未操作既有文件基础表或其它项目数据。清理回执在私有.local/relation-ui-d5a7e23479/.local。

## 多选择页配置候选断链修复（2026-10-19）

编辑器保留原选择器与缓存交互，单选/多选统一走真实管理员当前项目的专用候选；默认旧请求仍type=1，明确MultiSelectPage只返回type=2。页面与真实父表的项目/软删守卫保留，没有放宽原标准页parentId。协调者新增真实HTTP定向1/1后，完整API31/31、完整前端232/232均通过、零跳过；测试唯一配置聚合已按原API清理，无业务DDL。具体合同见[选择页配置候选](selection-candidates.md)。本轮未将未合并的Multi生成器或在线助手切片标完成。

## 冗余多选字段主线补验（2026-10-19）

主线集成多选原协议及子页文件范围后，生成/真实MySQL/Vue/完整包定向14/14、完整Files54/54、前端232/232零跳过；Release零警告错误、平台Web发行成功。双次元数据再生成215文件稳定、24手写partial不变。454文件独立包另验原字段弹层；实际发现视口溢出后修源头并重新打包/发行，完成取消/跨页四项/确认/真实保存回读、父切换旧项不可确认与空数组保存。详见[多选字段合同](multi-select-field-contract.md)。临时服务、认证配置与两张239行随机样本表已核验精确清理，最终完整包结构0差异。此批不冒充完整生成套件、真机或正式认证/在线助手验收。

主线后续冻结版本（包含发布失败现场保护）已完成完整普通Generation **125/125**，0失败、0跳过；TRX核对total=executed=passed=125。首次安装专属两项本批显式排除，最近实际通过记录仍单独保留，未折算本批结果。历史Access denied未复现，其原持有者/根因仍未知，见[发布失败诊断](package-publication-diagnostics.md)。

## 前端沿用偏差纠正（2026-10-19）

用户指出生成业务页不应重写。已确认当前新简化生成模板未完整沿用原YZP页面，前端保真交付尚未达标。此前测试与截图仅保留其具体功能证据，不作原布局/交互一致性验收；纠正源头及检查项见[前端沿用纠偏](frontend-preservation-correction.md)。

## 2026-10-20 前序原界面、安全核心与独立实例 RR 记录

本节保留 748 等前序批次，不能作为后文 6dc/f934 新协调核心的同批证据。前序失败与计数仍按原批次保留，不合并重复 case。在线助手已恢复原首页三按钮与原确认，队列、本机显式preview/apply授权、固定动作/快照、短期票据和journal v3恢复均已集成；日常不要求下载JSON或手动CLI。实际队列/本机CLI/Vue证据见[在线助手](generation-assistant.md)，原按钮至真实已绑定工程的整链浏览器仍待验，应用回执不是服务端独立字节证明。

产品冻结f3740cd原账号/角色UI由集成者真实浏览器只读复验：后台名称、角色名称数组、密码掩码、旧密码/盐空回显及启用“是”正确，退出并刷新回登录页。**新增0、保存0**，不证明账号新增/改密或新账号登录；后续74889d5核心提取未重做该UI批次。私有回执`.local/original-manager-ui-86c57ce-20261020/control/browser-readonly-receipt.json`，SHA256 `9fdc6b61ed4d4d7e8a7e0c640219cea54c3b14153d08dc79d0dd6267206f108f`。原复选、名称及密码/启用桥接均由元数据和生成源维护，没有重排DOM/CSS。

| 批次 | 实际结果 | 私有证据（仓库根相对路径） |
| --- | --- | --- |
| 74889d5安全核心 | 13/13，零跳过 | `.local/security-core-security-integrated.trx` |
| 同冻结API | 71/71，零跳过 | `.local/security-core-api-integrated.trx` |
| 同冻结Metadata | 152/152，零跳过 | `.local/security-core-metadata-integrated.trx` |
| 同冻结Compatibility | 29/29，零跳过 | `.local/security-core-compatibility-integrated.trx` |
| 同冻结定向Generation | 69/69，零跳过，含原14项夹具失败修正后的合同 | `.local/security-manager-generation-focused-integrated.trx` |
| 最新已知完整前端批次（独立日志） | 260/260，零跳过 | `.local/manager-name-frontend-full-integrated.log` |
| 后续9bc4693非RR Agent | 161/161，零跳过，显式排除RR三项；含账号凭据真实HTTP/MySQL 3/3 | `.local/security-manager-agent-mysql-integrated.trx` |

前端日志自身没有Git提交标记，不能把该表中的260项自动归入748的源码批次。上述较新日志SHA256为`f4e4920a685ba99e05ceb7dff5e747bec71d57efac8ea95d2a8bc244e83972bb`；原`.local/final-role-gates-frontend.log`也真实260/260、零跳过，但属此前829阶段的历史记录，不再作为最新批次引用。

旧完整Generation `.local/manager-name-full-generation-integrated.trx`仍为**612总数、610执行、596通过、14失败、2个FreshInstall未执行**；两项缺专属空库配置而明确跳过。原日志保留，后续定向69通过不能追改为完整612全绿，也不把未执行算通过。

冻结74889d5在Server-200独立临时MySQL实例经正式Bootstrap初始化两个新空库，不复制共享/客户数据：0/0表→平台23表/业务4表及3安全索引，RR前后结构快照同字节。RR三项实际3/3、零跳过：旧快照撤权后拒写且目标不变、后台变化冲突前不补新后台锁、无关锁仍持有时删除/工作台均提交。21个真实夹具参数计划全选正确索引，三组owned清理零残留；执行者为集成者，辅助Agent独立只读复核。贡献clone私有证据`.local/rr-isolated-20261020/execution/rr-isolated.trx`、`before-initialize.json`、`after-initialize.json`、`after-rr.json`及`source/.local/security-realm-rr/*/receipt.json`，详见[RR专项](security-realm-rr-validation.md)。

共享实例原150项仍为147通过/3RR失败；临时17列授权已撤销，管理端核验空、应用三检查MySQL1142，未重启/修改共享采集。成功批次结束后精确移除自有容器、卷、隧道及运行秘密/设置，长期备份按用户规范保留、不公开内容。清理回执为同execution下`cleanup-20261020053813.json`及`date-safe-tunnel-stop.json`；停止时仅单独修正ISO日期读取，已执行v2输入与seal保持，无重Run。

该前序批次当时仍保留独立生成 Host 完整安全 Runtime 门禁、角色 selection/export 用途 Gap 和平台 5 个 Custom 页面独立宿主门槛；用途边界应由各自后续切片单独验收，本节不提前调整。账号原页浏览器完整写入、所有配置入口撤权一致、生产认证/Agent 下发与授权浏览器、Tiny Cloud 编辑、跨端设备、Java 同资源性能和后续多库未据本批完成。

## 2026-10-20 原账号凭据HTTP/MySQL后续批次

冻结`9bc46930a79e0841e3467eb2dbdffca5f8044bc9`的非RR Agent完整161/161、零跳过，明确过滤RR三项；RR仍单列此前748冻结的3/3，不能声称同一批164。三项ManagerCredentialHttpMySqlTests通过真实TestServer认证/原完整表单/实际Guard与MySQL：新增生成PBKDF2且wire启用1对应物理0；mask及空salt/oldPassword更新保持摘要，省略角色保持原值；真实登录/注销；非法凭据、废弃字段非空、直接Service提交mask均拒绝并全行hash不变；仅自有账号及其session精确清理零残留。回执`.local/security-manager-agent-mysql-integrated.trx`，SHA256 `1305be1bab58cc1df5a41b73e8b4e56c183d6ac5dea6ee2e7b7be2d66a279b6a`，日志同名。

本批是HTTP/TestServer与真实数据库证据，不是浏览器新增/改密操作；不回写f374只读批次。运行前共享实例及观测权限只读复核仍正确，三项检查仍1142，私有回执`.local/rr-observation-preflight-20261020/manager-http-server-pin.json`；公共文档不公开实例标识或连接。独立宿主 Runtime 及用途能力门槛不因平台 HTTP 通过而自动移除。

## 2026-10-20 协调核心下沉后的 6dc/f934 分批验收

本节更新新协调核心的阶段实证，保留旧 748 成功、共享 150 项失败和完整 Generation 612 项失败，不追改历史，也不将不同冻结/过滤器的结果累加成一批。上表 23 表、649 列、20 页、665 页字段计数继续按既有平台自举记录保留。

C# 冻结为 `6dc09a4e11f72d46513cddd5a7d8e8428413b723`，Api Release 构建零警告、零错误。主仓库私有证据如下，均实际执行且零失败、零跳过；含 MySQL 的测试工程也含离线合同，工程用例总数不是锁场景总数。

| 范围 | 本批实际结果 | 原始报告（主仓库 `.local/`） |
| --- | --- | --- |
| Security 核心 | 13/13 | `security-coordination-core-integrated.trx` |
| 非 RR Agent | 182/182，排除 RR 三项 | `security-coordination-agent-mysql-integrated.trx` |
| Metadata | 152/152 | `security-coordination-metadata-mysql-integrated.trx` |
| API | 71/71 | `security-coordination-api-mysql-integrated.trx` |
| Compatibility | 29/29 | `security-coordination-compatibility-integrated.trx` |
| Generation MySql 类定向 | 56/56 | `security-coordination-generation-focused-integrated.trx` |
| 另一次 Generation Contract 补验 | 14/14 | `security-coordination-generation-contract-integrated.trx` |

Generation 首次过滤器选中的实际数量为 56，另补 Contract 14；不是此前 69 项同一批，也不能因此把旧 612 项完整失败记录写成全绿。汇总 `.local/security-coordination-integrated-receipt.json`，SHA256 `ceef1d5f7d86bf7c3c8569aeb6ee8e06b420931f99ad971df3db95aa77ff12e8`。

随后只合 Node builder 顺序修复，RR 归档冻结为 `f934a87425d0885aab841b7d91ab77bc4017bd62`。集成者在 Server-200 的 P_S ON 新独立临时实例正式初始化两新空库：0/0 表→平台 23 表/业务 4 表及 3 个安全索引。三个 RR 场景实际 3/3、零跳过；21 个真实夹具参数 EXPLAIN 全部命中相应前缀索引、访问 ref，三组 owned 清理零残留，RR 前后结构同字节。具体场景和证据见 [RR 专项](security-realm-rr-validation.md) 的 f934 批次，不用旧 748 结果替代。

贡献 clone `.local/rr-isolated-security-A-20261020/execution/rr-isolated.trx` SHA256 为 `79618b2ce901f0d8bf2a8e4501e89c718934bd86c20064e5b11d9f638bf1e06b`，独立核验汇总 `coordinator-verification.json` SHA256 为 `f92e5c77d264af9cd5006dac5cbe934b87f00b2088833dbfc68f274b3ae539d7`。新 sealed helper 的 Stop/Cleanup 已实际成功，DateKind 字符串停止路径首次正式通过；`cleanup-20261020062213.json` 及独立核验证明自有容器、卷、远端运行 secret、隧道和本机运行 settings/secrets 全清，长期备份按用户规范保留。旧 v2 输入、seal 和执行回执没有改动。

截至本次文档记录，f934 的 CI 为 4/6 成功：两个前端、两个 Linux 已成功，两个 Windows 作业仍在运行，不能称六绿。Linux 完整 Generation 为 603 通过、12 跳过、总数 615；CI 缺私有 DB 配置的跳过不算实际数据库通过，不能替代上述本机 Server-200 证据。后续作业状态需由集成者单独核实更新。

这些结果只完成该阶段实证，不表示独立生成 Host 的正式认证、显式账号初始化、完整安全 Runtime/业务下沉或所有配置写入口已经完成；安全物理模型目前也只是显式派生准备。账号浏览器完整写入、用途能力的独立验收、跨端设备、同条件 Java 性能及后续多库仍须分别取得证据，不据本批关闭门槛。
