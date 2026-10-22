# 实施与验收台账

来源：YZP 工作区 `D:\\svn\\yzp`，参考提交 `72c46ca6`。验收更新：2026-10-22（前序记录保留原日期）。独立目录 `E:\\git\\metarill`，公开仓库 [zhao-yc/metarill](https://github.com/zhao-yc/metarill)，开发分支 `codex/initial-platform`。旧 Java 工作区未修改、未部署。

## 当前结论

首期 C# 基础与 MySQL 核心纵向闭环可开发、可审查；**完整 YZP 能力、前端跨端及性能验收尚未完成，不作为正式版本发布**。普通数据访问采用 FreeSql；配置、权限、文件等基础设施以同一 DatabaseSession 的参数化命令加入事务。

当前原页面、样式、交互及模板恢复的证据以 [前端恢复与原协议台账](frontend-preservation-correction.md) 为准；下文前序简化页面记录不计为原界面验收。角色规则已按用户确认沿用原V8页面操作权限及目标后台归属，不增加超级身份门槛，见 [角色管理规则](role-assignment-policy-decision.md)。原角色复选读取及同后台工作台入口已恢复，标准安全表写协调已接线且完整实库回归通过，独立实例三个RR场景已验；原账号/角色名称、掩码和启用只读补验，后续凭据HTTP/MySQL三项已验；原账号新增/改密/登录/软删的浏览器基础闭环已补验，退出的服务器会话撤销也已补验；其它账号规则及独立宿主安全闭环仍待验，不能以早期双重平台管理员门禁作为最终实现。

| 能力 | 已取得证据 | 剩余边界 |
| --- | --- | --- |
| 仓库、许可证与环境 | 独立历史、公开仓库、Apache-2.0；官方 .NET 10.0.402 SDK 校验并安装 | CI 结果另以实际 Actions 为准 |
| 平台自举 | 23 张表、649 列、20 页、665 个页字段；固定标识清单；重复初始化不复活逻辑删除记录 | 首版不迁移旧业务数据与代码 |
| MySQL 核心流程 | 真实登录→配置表/批量字段→结构预览/执行/二次核验→生成 ZIP→业务 CRUD | 浏览器已验临时表保存、字段配置、结构执行和生成下载；原选择器许可已确认，从零纯界面选择后台、建表及默认字段回读已通过 |
| 生成与独立应用 | 强类型 DTO/VO/Mapper/Service/Controller；严格字段存在性；受保护 partial；独立应用包真实编译及 MySQL CRUD/复制/事务；FullApplication 已组装独立 uni-app 宿主，默认数据库 Web 认证与显式安全初始化已真实全新库验收，平台显式 BackendOnly 保留开发模式 | 共享导出、本地文件与同库引用事务已验；历史开发登录/菜单/生成 CRUD 的真实 HTTP 脚本通过；独立包已实测 Web 发行、主子 Tab/PC 弹层和独立多选页浏览器；完整安全 Runtime A/三个手写 partial、五处平台 Custom、Agent及正式 Web 浏览器仍未完成；文件、复杂关系导入导出、下载落盘和真机仍缺；原单表导入及服务端XLSX已有实证 |
| 菜单、权限 | 单表树、页面归属后台；项目行锁/版本冲突；导航与页操作分权；真实身份后台读取；凭据HTTP/MySQL三项 | 原角色复选读取及同后台工作台入口已恢复；平台标准安全表协调已接线，独立实例RR三场景已验；原账号基础写入与改密登录/退出浏览器闭环已补验；其它规则及独立宿主下发仍缺，不把菜单可见当写授权，不以额外平台超级门槛替代原规则 |
| 配置工作台 | 页面布局、字段批量开关/排序、批量建字段、索引聚合、页面复制/创建、快捷建菜单；原 V8 索引引用拒绝删列及整表回滚已真实 MySQL 补验 | 保存配置不会自动执行 DDL；高级联接、物理视图等尚未移植 |
| 结构同步与只读反查 | 指纹/短期计划、防漂移/重放、MySQL 锁、结果审计；部分 DDL 失败明确记录；HTTP/MCP 受管结构反查固定 executable:false | 不自动写元数据或执行反查 SQL；独立宿主已提供固定受管模型 --schema-inspect；平台 CLI 选项目、未登记表目录及反向导入仍待完成；不删除未声明结构，不支持索引自动替换/删除、DESC，破坏性变更拒绝 |
| 文件 | 真实上传/完成/解析/访问、目录操作；业务引用同事务；删除与引用并发保护 | 当前本地存储；平台/已发布示例业务库接线与同库引用事务已通过；原RTF/ImageUpload URL尚不追踪引用生命周期，S3待实现 |
| 导入导出 | 前端行数据导入整批回滚；真实 XLSX；身份绑定短期下载；读前并发/票据门禁与 SQL 上限 | 复杂控件/关系按能力清单明确拒绝；不静默截断 |
| MCP | 官方 C# SDK 2.2.0；匿名拒绝；工具列表、结构预览和同 Service 页操作通过真实客户端 | Agent 的 MCP 写操作当前拒绝，写入走带任务保护的能力 API |
| Agent | 设备码授权、刷新轮换/撤销、能力目录、写确认、持久任务 UUID 去重；实际 CLI 端到端通过 | 人工确认由隔离测试账号模拟协议，尚无真实浏览器授权操作验收 |
| 前端 | 新AppID与HBuilderX Web发行；原登录/导航/单表CRUD/导入、配置表及选择器建表；原账号角色只读补验；最近完整前端273项 | 原账号基础新增/改密/登录/退出/软删已补验；其余业务、小程序/Android/iOS真机待验收；Tiny Cloud 按用户决定空key，真实加载/编辑待配置；本机 HBuilderX 预览入口未启动，使用发行产物静态服务验证 |
| 性能 | 保留固定类型热点查询/映射；导出实测 LIMIT 上限和满额不读库 | 未完成与 Java YZP 同数据/SQL/索引/并发/资源的对照，不作性能达标结论 |
| 多数据库 | Provider、逻辑类型、能力、结构计划与平台/业务连接边界 | 目前只实现 MySQL；MariaDB/PostgreSQL/SQL Server/Oracle/SQLite 尚未支持 |

## 验证方式

协调者在 Server-200 的 `metarill_test` 与 `metarill_business_test` 两个独立库运行全部后端测试，凭据不提交；TRX 保留于私有 `.local/acceptance-results`。前端运行时与 CLI 测试使用 Node.js，真实 CLI 另以测试后端验证设备授权、目录、预览不写入、确认新增、任务查询、重复 UUID 拦截、逻辑删除及撤权。

CI 在 Linux / Windows 重新生成、比对提交产物、编译宿主并遍历所有测试项目。CI 不持有私有 Server-200 凭据，无隔离配置的 MySQL 用例明确跳过；绿色 CI 不等价于数据库或设备验收。

`--synchronize-development-seed` 仅用于本次新建开发/测试库的种子标识校正，限定库名并先备份包含逻辑删除的元数据快照。它会替换内置平台与示例元数据，**不能用于已有用户项目或普通初始化**。备份已独立保留，不包含原 YZP 业务数据。

## 待验收顺序

1. 原单表新增/修改/复制/软删及合法/坏批次导入已补验，原账号基础写入、改密登录、服务器退出与软删已补验，继续其它账号规则和其余页面；原选择器许可与接入、新AppID、Web发行均已通过。自有 Tiny Cloud 保持空key，真实编辑按用户要求留待后续。
2. 主子 Tab/PC 弹层与独立多选页已完成独立包 Web/浏览器核心验收；继续List关系大数据、文件、复杂关系导入导出及下载落盘；补齐高级联接、独立宿主完整安全协调/Agent和已接在线助手的原按钮整链；数据库 Web 与显式初始化已有下述新实库证据，正式登录浏览器及安全写仍须补验。
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
| Generation 定向（含 MySql 类） | 56/56 | `security-coordination-generation-focused-integrated.trx` |
| 另一次 Generation Contract 补验 | 14/14 | `security-coordination-generation-contract-integrated.trx` |

Generation 首次过滤器选中的实际数量为 56，含 MySql 类但不表示 56 项全为实库动作；另补 Contract 14，不是此前 69 项同一批，也不能因此把旧 612 项完整失败记录写成全绿。汇总 `.local/security-coordination-integrated-receipt.json`，SHA256 `ceef1d5f7d86bf7c3c8569aeb6ee8e06b420931f99ad971df3db95aa77ff12e8`。

随后只合 Node builder 顺序修复，RR 归档冻结为 `f934a87425d0885aab841b7d91ab77bc4017bd62`。集成者在 Server-200 的 P_S ON 新独立临时实例正式初始化两新空库：0/0 表→平台 23 表/业务 4 表及 3 个安全索引。三个 RR 场景实际 3/3、零跳过；21 个真实夹具参数 EXPLAIN 全部命中相应前缀索引、访问 ref，三组 owned 清理零残留，RR 前后结构同字节。具体场景和证据见 [RR 专项](security-realm-rr-validation.md) 的 f934 批次，不用旧 748 结果替代。

贡献 clone `.local/rr-isolated-security-A-20261020/execution/rr-isolated.trx` SHA256 为 `79618b2ce901f0d8bf2a8e4501e89c718934bd86c20064e5b11d9f638bf1e06b`，独立核验汇总 `coordinator-verification.json` SHA256 为 `f92e5c77d264af9cd5006dac5cbe934b87f00b2088833dbfc68f274b3ae539d7`。新 sealed helper 的 Stop/Cleanup 已实际成功，DateKind 字符串停止路径首次正式通过；`cleanup-20261020062213.json` 及独立核验证明自有容器、卷、远端运行 secret、隧道和本机运行 settings/secrets 全清，长期备份按用户规范保留。旧 v2 输入、seal 和执行回执没有改动。

此前记录的 f934 CI 4/6 是运行中快照；现集成者核对 [PR run 39378960357](https://github.com/zhao-yc/metarill/actions/runs/39378960357)（最终更新时间 `2026-10-20T06:32:13Z`）与 [push run 39378955800](https://github.com/zhao-yc/metarill/actions/runs/39378955800)（`2026-10-20T06:29:26Z`）均为 head `f934a87425d0885aab841b7d91ab77bc4017bd62`，两个前端、两个 Linux、两个 Windows 共 **6/6 作业最终成功**。CI 缺私有 DB 配置的跳过不算实际数据库通过，不能替代上述 Server-200 证据；这组 CI 只属于 f934，不自动验证随后 d696/e420/edadf 用途切片。

这些结果只完成该阶段实证，不表示独立生成 Host 的正式认证、显式账号初始化、完整安全 Runtime/业务下沉或所有配置写入口已经完成；安全物理模型目前也只是显式派生准备。账号浏览器完整写入、用途能力的独立验收、跨端设备、同条件 Java 性能及后续多库仍须分别取得证据，不据本批关闭门槛。

## f934 最终 CI 原始计数与随后角色用途切片

集成者读取两 run 最终状态及四个后端作业原始结果行，私有汇总 `.local/ci-f934-final/receipt.json` SHA256 为 `c0ec5c22971f2f638ec3d43a016d2b2c817b74c3cb21bbedc2d85b190a93631d`。Linux 两作业计数相同，Windows 两作业计数相同；下表按单个相应作业记录，不叠加成实库场景数。

| CI 工程/平台 | 通过 | 跳过 | 总数 |
| --- | --- | --- | --- |
| Generation / Linux | 603 | 12 | 615 |
| Generation / Windows | 604 | 11 | 615 |
| Agent | 163 | 22 | 185 |
| Security | 13 | 0 | 13 |
| Data | 41 | 4 | 45 |
| Compatibility | 18 | 11 | 29 |
| API | 44 | 27 | 71 |
| Metadata | 130 | 22 | 152 |
| Files | 38 | 17 | 55 |
| Exports | 28 | 0 | 28 |

平台固定角色/后台名称的 selection 与 export 用途随后已接线：源码提交 `d6967d96d1464cf3acc10b90cd7d66eca11bb1e4`，正式生成提交 `e4209a139103d60da43452ec66e63b92cd458b65`，合同说明集成点 `edadf60719907fbc687326889d6f85313a7a5ebf`。不再把该固定图的两种用途列为当前未接线 Gap；未知配置及独立宿主门槛仍按真实编译结果拒绝。原角色数组导出保持 ID JSON 文本协议，不转换成角色名称拼接。

主仓库 `.local/security-read-uses-regeneration-integrated/receipt.json`（SHA256 `a8ba9b8572374269802ee26435108a97d532503a4d0081e9c9f6d36fa2fe140e`）证明两轮 191 个受管文件与清单字节稳定、24 个手写 partial 不变，Api Release 零警告/零错误。另有两份实际测试报告，均零失败、零跳过：

- `.local/security-read-uses-generation-integrated.trx`：136/136，SHA256 `9aea272f9e7e0a4f208ba5c74c5d0e9d6117dce83f9b75c2cca96818d5afd838`。
- `.local/security-read-uses-agent-mysql-integrated.trx`：非 RR 182/182，SHA256 `8a7d8dd4dabee490302f6d9b09aa15be2ac87206416963ac9b0e08df3ff1b646`。

运行前 `.local/security-read-uses-server-pin.json` 已实时核验共享实例指纹和三条观测权限检查 1142，未新授权或创建账号。上述 d696/e420 冻结当时新增的 5 个用途合同用例属于离线真实生成 SQL/Excel 证据，不能将非 RR 182 项回归冒充后续专项实库通过；新的角色选择/导出 MySQL 专项已在随后 4564b68 冻结单独完成，见下节。独立 Host 完整 A 下发及五处平台 Custom 独立宿主门槛仍未关闭，浏览器完整写入、性能和设备验收也不因此完成。

## 2026-10-20 c141 最终 CI 与固定读取用途实库专项

两组 CI 同属 head `c141bd3f196d7fd5abc34c89e9871d6dee537d94`：[PR run 39382659610](https://github.com/zhao-yc/metarill/actions/runs/39382659610) 最终更新时间 `2026-10-20T07:13:59Z`，[push run 39382654037](https://github.com/zhao-yc/metarill/actions/runs/39382654037) 为 `2026-10-20T07:12:10Z`，两组各 Linux、Windows、frontend 共六个作业全部 success。主协调者已读取四个后端作业原始结果行，每作业九个测试工程；私有汇总 `.local/ci-c141-final/receipt.json` SHA256：`fe6c6c96519e408d65fc4bcbb0708c56ed7814cc025f7ae02316f4fbb30bec7d`。

| c141 CI 工程/平台（单作业计数） | 通过 | 跳过 | 总数 |
| --- | --- | --- | --- |
| Generation / Linux | 625 | 12 | 637 |
| Generation / Windows | 626 | 11 | 637 |
| Agent | 163 | 22 | 185 |
| Security | 13 | 0 | 13 |
| Data | 41 | 4 | 45 |
| Compatibility | 18 | 11 | 29 |
| API | 44 | 27 | 71 |
| Metadata | 130 | 22 | 152 |
| Files | 38 | 17 | 55 |
| Exports | 28 | 0 | 28 |

该 CI 不持有私有数据库配置，跳过仍不算实库；它不包含随后 `41005f2d71f7e410f199d79a7929983285492e30` 新增、根合为 `4564b6893bdc7454a74b592eb9d9bcf00b96b33f` 的专项测试，不能将两批混为一批或回写 f934 的 615 项历史统计。

4564b68 的 `FixedRoleReadUseMySqlTests` 由主协调者独占执行 **5/5，零失败、零跳过**：两项离线，三项真实 TestServer/普通账号认证/MySQL 场景分别覆盖账号选择、角色选择、仅导出权限与原 IDJSON/后台名称的真实 XLSX 回读。生成读取 ORM 成功路径分别为账号选择 2 条、角色选择 2 条、账号导出 1 条/角色导出 2 条 SQL，不含认证/建样本/清理成本，不作性能验收。原始 `.local/fixed-role-read-uses-mysql-integrated.trx` SHA256：`a5e84f0f72ad7a412d22ab9a7b819151f29adaea09c7af888a5326f2b5eabd4a`，同名日志保留。

三份完成收据登记及清理 18/17/17 行；主协调者随后另行只读固定实例与全部 **52 个精确 ID/名称含墓碑零残留**，`.local/fixed-role-read-uses-verification-20261020/verification.json` SHA256：`88d2a2f6c6465bef8c7c1c1230628798189f98a58531df8adaff3bc75f5bcf38`。独立回查 v1 曾误按 number 解析规范字符串 ID 而失败，v2 只修解析并只读通过；两版源/日志保留，没有重跑测试或改数据库。原预检三张观测表均 1142，本专项无 GRANT、不依赖观测权限。完整收据摘要和边界见 [固定角色读取用途 MySQL 专项](testing/fixed-role-read-mysql.md)。

平台原开关、canonical/shared 配置元数据及原 DOM/CSS/事件不变；三个实库场景不算浏览器验收，也不解除独立 Host 完整 A 下发、五处 Custom 前端依赖或新 Host 的门禁。账号浏览器完整写入、RR、性能、跨端设备与新 Host 仍按各自冻结及证据单列。


## 2026-10-20 索引引用删列规则实库补验

冻结源 `2fd46f89a081b09d3532ed43381ebffc6ccd55f6` 恢复原 V8 对象规则：活动的索引字段引用阻止删列，父索引禁用、软删或缺失不能隐藏该引用；子行自身软删则不阻止。整表聚合先处理自有索引/子行，再逐列按同一规则检查，跨项目直接从属或残存外部/悬空引用全事务回滚。Server-200 专项原 HTTP 流程已验证活动与禁用引用拒绝、失败时完整自有图不变、显式移除引用后删列，以及整表删除和精确清理，不执行物理 DDL。规则与证据详见 [索引引用阻止单列删除](column-index-delete-parity.md)。

| 此冻结实际执行批次 | 通过 | 失败 | 跳过 |
| --- | --- | --- | --- |
| 独占索引删除 MySQL 专项 | 1 | 0 | 0 |
| API 完整工程 | 72 | 0 | 0 |
| Metadata 完整工程 | 166 | 0 | 0 |
| Agent 非 RR | 187 | 0 | 0 |

专项 1 项已包含在 API 72 中，不重复累计；完整工程同时包含离线规则/合同测试，不把每项皆称为 SQL 场景。非 RR 187 包含此前 5 项固定角色读取用途，与早先非 RR 182 分开保留，不相加。Api Release 零警告/零错误；本批没有重跑 RR、浏览器、性能或设备验收，也不据此改变独立 Host 的阶段边界。

原始回执为私有 `.local/column-index-delete-mysql-integrated-v2.trx`、`.local/column-index-delete-{api-full,metadata-full,agent-nonrr}-integrated.trx` 及同名日志，Release 日志为 `.local/column-index-delete-api-release-integrated.log`。首轮专项 0/1 是夹具 `sortNo=0` 在工作台事务前被输入规则拒绝，原 TRX/log 保留，未写索引；测试输入/回执修为 10 后独立 v2 才通过。旧夹具的专用 Plan→Execute 清理回执在 `.local/column-index-delete-failed-fixture-cleanup-20261020/`：24 元数据加 1 会话，22 原快照、25 锁后指纹均匹配，无未知引用，含墓碑零残留，无 DDL/授权操作。失败与清理均未计为通过项。


## 2026-10-20 独立Full正式Web与全新库补验

独立 Full 的 `web-database-v1` 已接通数据库账号、PBKDF2、持久化 Web 会话和单独 `--initialize-security`；普通启动不 DDL/初始化，无配置或无效身份不回退开发账号。BackendOnly 仍为原 development-only，安全 CRUD/三份手写 partial、完整 Runtime A、五处 Custom 和 Agent 不据此解除门禁。此前开发认证的浏览器证据不能移用为正式 Web 登录验收。

主协调者 `.local/standalone-web-authentication-integrated.trx` 实际 **84/84、零失败、零跳过**；正确真实类名的 CLI 准备/拒绝 scope 覆盖用例随后单独 **1/1、零跳过**，见 `.local/standalone-web-authentication-cli-bridge-integrated.trx`。首个错误类名过滤未匹配不计通过，首批仍是 84；此两批是本机替身、包、脚本和真实 CLI 进程，不称全部实库，也没有把旧 updater 64 项的 63/1 历史改成全绿。

Server-200 专属全新库 `1ce30f018ba84e4e` 实际 **2/2、零跳过**：Full 从 0 表显式安装 10 表（本单业务表＋文件 3＋安全 6），重复无差异，真实 Web 模式初始化两次摘要保持，再真实 HTTP 登录/菜单/权限和文件引用/受保护下载；另平台初始化仍是平台 23 表、业务库 4 表，重复不重置并能登录。私有 `.local/fresh-install/1ce30f018ba84e4e/{result.json,fresh-install.trx}` SHA256 分别为 `65d3138a2c39c109dc6efe79a8f813d34b6c6741dadaec3f857a27db936cc4ee`、`5cb836e32baedf60d9b48c8fac8ddfb9a0440159a08dd0e4ee12e775a0781610`。脚本清理后另行只读回查三库/临时账号为 0、所有权文件不存在，`cleanup-verification.json` SHA256 `3aa97f1cd6910503e8a29271f4a6011c4328ef9a0dc9feb50abe5ec089eb5063`；本机凭据文件已删，不保留可用测试身份。

487 文件独立新包还通过自身 Release `-warnaserror` 零警告/零错误、离线 npm 安装、Node 闭包和 HBuilderX 5.26 Vue3 Web 发行；只在私有验收副本填写已授权 AppID，源模板不改。包与命令/日志见 [独立应用包](standalone-application.md)。该新包未启动 HTTP 服务或进行浏览器操作，发行不替代正式登录 DOM、完整账号/角色写入、RR、设备、性能或部署验收。

综合私有回执 `.local/standalone-web-authentication-integrated-receipt.json` SHA256 `398b59656f39fa683b78d938ee3ae6688907bbe79604456cc31bd73c9cca3a15` 分列 84、补 1、Fresh 2 及所有未验边界；不将这些不同批次替换为一个完整套件通过数。

## 2026-10-20 4af8875 独立Full正式Web顺序补验

本批在测试源码 `3c8ed14d2d8a17f8f7927a4830d0897437a47391` 集成为 `4af8875c411a4e0c03ef2d6596f2d6d055f53f3e` 后独占执行。Server-200专属脚本编号 `228fdfff592c4283`，仍为两个Fresh Facts、三个随机新空库和一个临时受限数据库账号，实际 **2/2、零失败、零跳过**；不替换前节1ce30结果或累计重复运行。平台Fact保持23/4表，Full从0表显式安装10表、二次0差异及原文件CRUD/受保护下载均保留。

Full追加合同在原文件/业务CRUD之后，真实启动该包Web进程并走原HTTP与MySQL：普通query-only账号真实登录、原菜单/页读允许；同一 `POST initExport` 先由bootstrap返回200票据，再由普通账号403，无权新增403且原数据不变。伪客户端项目/后台/超级上下文不能扩权，自己的Agent类型会话被拒；禁用与撤query在下次请求分别401/403。错误旧密码不改摘要，两个原先真实有效的Web令牌改密后均401且该账号活动session归零，新密码登录/旧密码拒绝。

bootstrap本人已改密码不会被原初始化配置重置；真实进程重启后，普通账号改密后签发的原持久令牌仍可读页/回读query权限，退出后401且活动session为0。普通角色、账号及授权为完全自有package库的明确夹具种子，不伪称走了尚未下发的安全管理CRUD。整个场景是顺序HTTP/数据库验收，没有新增RR/并发锁或浏览器DOM结论。

私有 `.local/fresh-install/228fdfff592c4283/` 的证据分别为：

- `result.json` SHA256 `0b5b2a298eae43a5945807bdbd3cbdd47c9a74918b137e036cc334a87a00b3a3`：2通过/0失败/0跳过、3库及1临时数据库账号均已清理、凭据文件已删。
- `fresh-install.trx` SHA256 `4163824c1602ea1e0f5096fc796036f2f9e59c9b111f1dce693d088a6dd80729`：仍是两个原Fresh方法，另保留 `test.log` 与0警告/0错误的 `build.log`。
- `cleanup-verification.json` SHA256 `57900dcdcb76eac5936b57a4d954b41f311c74e4c543f83c39156e86593d362d`：独立只读回查剩余库0、账号0、所有权文件不存在；本机 `settings.json` 已不存在。

另一个 `.local/fresh-web-http-integrated.trx` **5/5、零跳过**仅为无端口/无数据库的HttpMessageHandler协议合同，SHA256 `4d17ff10d02c111513cbda63451f35e655fd0475fcff3f0d781708d2e37faa1d`；不将这五项称为实库或并成七项Fresh。完整细项见[正式Web宿主](standalone-web-authentication.md)。

原84+单独1、1ce30两项与487文件HBuilderX发行证据均保持各自冻结与边界。共享Security中的角色工作台Business存在不等于独立宿主已注册完整目录证明/协调器/安全CRUD；Runtime A、三份安全Service手写partial、五处Custom、Agent、浏览器完整账号写入、设备和同条件性能仍按各自未验收门槛处理。

## 2026-10-20 d6 工作台核心提取根集成回归

`RolePermissionBusiness`由源码 `4d4ec20729c342712655dd7d5621f531a76e4e6e` 提取至共享Security，根冻结 `d6d0594307682d19107b791a9cf213daeead6321` 的实际集成结果为：Security13/13、Full/profile8/8、Agent非RR v2完整195/195，全部零失败/零跳过；Api Release零警告/零错误。Agent195中包含22个真实MySQL用例，同时也有离线合同，不把195项全称为实库场景。

首Agent因漏平台元数据环境路径，真实为173通过/22跳过/0失败，原报告保留；仅补22的命令自动审批超时未启动，不计执行。最终固定简单filter重跑完整195才零跳过通过，不累计重复。原始 `.local/role-permission-core-{security,agent-nonrr,agent-nonrr-v2,profile}-integrated.trx` 及同名日志、`.local/role-permission-core-api-release-integrated.log` 分批保留，完整合同与边界见[工作台核心提取](role-permission-core-extraction.md)。

本批没有新增RR、观测授权、前端、浏览器或设备验收；共享Business存在和平台实库通过不等于独立Full完整安全Runtime、目录权威/协调器、安全CRUD或Agent下发，五处Custom与原门禁保持。后续SessionPolicy与目录适配的独立结果不提前计入本批；最新CI也不借此声明完成。

根综合回执另保留纠错：旧 `.local/role-permission-core-integrated-receipt.json`（SHA256 `706731774d78c7f26ee897f6b0b668c92b1ea595fb6b6503b0b0025790085750`）曾按VSTest `Counters.notExecuted=0`误记首轮skip；原件未覆盖。新版 `.local/role-permission-core-integrated-v2-receipt.json`（SHA256 `7664b447c31dc217424cf831fbf52bbfa2486875c6ff5a881f314bedf1559e74`）按实际 `UnitTestResult`记录首轮22跳过，并保存原rawCounters和纠错说明；四份TRX及原日志不变。上述计数以真实结果项及原日志为准，不把汇总的旧零值当作已执行。

## 2026-10-20 固定目录、正式协调与三页模块根集成

固定目录源码 `0915392` 已集成为 `b576438`，正式 Web 协调源码 `1b4bb93` 集成为 `255787d`，固定三管理页资产/工厂源码 `b5d7e4f` 集成为 `14cf2542d5364f1fe59ee59aeacbd41112ecfeee`。原页面、样式、交互和权限规则不变；可信模块只准备原三页和显式依赖，尚未接默认 Full 管理 CRUD、工作台或原初始化按钮，Runtime A/三个 partial/五处 Custom/Agent 门禁仍保留。

主协调者实际执行，按冻结和范围分列；重叠批次不相加：

| 源码冻结与范围 | 通过 | 失败 | 跳过 | 私有原始报告 |
|---|---:|---:|---:|---|
| b2023bd 固定目录与 CI 修复组合定向 | 206 | 0 | 0 | `.local/immutable-ci-merged-contracts-integrated.trx` |
| 750dddc 完整 Generation 离线 | 732 | 0 | 11 | `.local/generation-750-full-offline/generation-750-full-offline.trx` |
| 14cf254 正式 Web/可信模块/物理合成组合 | 152 | 0 | 0 | `.local/web-coordination-trusted-module-integrated/web-coordination-trusted-module-integrated.trx` |
| 14cf254 元数据 Node 与实际 `--check` | 17 | 0 | 0 | `.local/trusted-security-module-node-integrated.log` |
| 14cf254 Server-200 全新库两个 Facts | 2 | 0 | 0 | `.local/fresh-install/e2a43246e91c4cae/fresh-install.trx` |

完整 Generation 共743项，耗时16分42秒、`-warnaserror`；11项因移除实库配置而未执行，不算实库通过。该批消除了本机此前14项兼容失败，但不回写8eda两个CI run的原失败结果；随后正式协调与模块另在14cf进行152项组合回归，含实际生成包编译及离线身份/ADO替身，不称152项全部实库。正式远端 CI 以 PR #1 对应提交的实际作业结果为准。

新 Fresh 编号 `e2a43246e91c4cae` 独占创建3个随机空库/1个受限临时账号。两个 Facts 实际全过：Full从0表显式安装10表，普通启动/预览不DDL、二次0差异，正式Web登录、普通账号逐次撤权/禁用、改密撤两个旧会话、重复初始化不重置、进程重启持久身份、注销及文件/业务CRUD保持；平台另例仍23/4表。该例没有发布三管理页，不能据此声称安全CRUD、工作台或独立宿主RR已完成。

脚本清理后，主协调者另经核实的Server-200逐条只读回查：本轮3库剩余0、临时账号0、所有权文件不存在，本机临时配置已删；本轮未新增P_S或PROCESS观测授权。新Fresh的 `result.json`、TRX和 `cleanup-verification.json` SHA256依次为 `85ac27b6116e0722d731f709f447c116ec541fbc54eea985200acc664100b1d0`、`03d0f0447368336db597a157bf43cd5a1799be95dfc842c8113d9f592c6c79e3`、`f969816a1ea738770e80274cc5a52d057ff13ce27bffc7d1bcfd28d91614e266`。

主汇总 `.local/immutable-web-module-integrated-receipt.json` SHA256为 `62dc72224db54939d14bd33252c8da107c65af72200b13856e71496b9e040e7a`，按真实 `UnitTestResult`计数并保留早先失败/跳过；完整Generation TRX摘要为 `d41d4531bd97daad932fc181c08db0d44ae71643df9653feb6dbdbdc4e440cfa`，152项为 `e26f86851bfda01ba599b83b85347c3b8ee7c2b82f5799ac2ee606355914be8a`。三原按钮到绑定工程的真实浏览器、账号页完整写入、新宿主RR、设备、同条件性能及后续数据库仍单列待验，不能用本批构建/HTTP结果代替。

## 2026-10-20 三页准备回归及原首页真实按钮排障

根冻结 `6677829` 的权限拒绝兼容为22/22，`d46ac893d4083f0b93cc73a55d03f668d41919a9` 三 partial 组合87/87，`e44d719b200a63da63abd19f86c7fd86ff073f09` 隐藏项目候选组合80/80、原工作台规则53/53，均零失败/零跳过。各批有重叠，不累计；它们使用真实生成编译、共享规则及离线身份/ADO合同，不宣称完整HTTP或数据库管理页验收。原始报告分别位于 `.local/{role-option-permission-denials,trusted-security-partials,trusted-admin-project-options,role-workbench-original-parity}-integrated/`；SHA256依次为 `941eafd10e8bbcf4778d2cf17c47e8a8db562c8cf399fa66882d97f69611da00`、`ecaceef380a0401a23b3b34f9bf9ed1a89c0c916f329d2ce11f9038c102f1bd1`、`ed5233100586f82b1191de33d0bd1ab75adbb82fbfbac3980f43c36d6231a87c`、`0796a0789eda91dee124bcbcb0ac0c23df65242ba599dfd5f3e006736bd7e1f7`。

此前冻结 `c4abff4223e4ed583275e5154aec2f75ec2cf32e` 的 PR/push CI run `39411918626`、`39411909726` 已分别三作业成功，合计6/6；每个后端作业9个测试工程均无失败，缺私有配置的用例仍跳过。私有 `.local/ci-c4abff4-final/receipt.json` SHA256 `bc015690c8736ed5f86ee0d1b6752edf7579548007cf57f105e019ae238a9c7f`。该结果只对应c4，不能挪作以上后续提交的远端CI结论；8eda原失败记录保留。

真实Ego浏览器使用固定d46后端/CLI与原管理前端，完成原登录、设备/项目选择、首页及本轮自有助手应用授权。原会话只读导出的Full ZIP为4,422,749字节，SHA256 `91c589e4336f2cc5a8a926bcb447965c1be9457c88975e5cab2e1d43edda9ebc`；正规896旧包的487文件目标严格身份检查通过。真实导出候选的三个单范围预览均提示需要其他两个范围；没有手工修改受管生成文件制造差异。首次原确认取消后，独占队列仅一个本轮绑定，任务及组文件均0。

随后首次实际确认生成项目配置，页面报 `getStorageSync is not a function`，尚无任务入队。实际HBuilderX产物将明确的 `uni.getStorageSync` 改为按需导入调用，但传给桥接的对象简写仍引用原始全局 `uni`。首页只增加五个明确方法的转接，供编译器转换，保留原模板/样式/确认与三个入口。新回归在修复前6过1失败，修复后7/7；前端完整261/261、零跳过，日志分别为 `.local/online-browser-hbx-api-fix/{bridge-red,bridge-green,frontend-all}.log`。该离线修复结果尚需重新HBuilderX构建和真实三按钮文件应用验证，不能把登录、授权、预览或排队等同于完成。

本轮实际失败、取消截图、独立旧包及预览均保留在私有 `metarill-contrib/online-helper-live-a2e6b522e37e46b9/control`。未改元数据、账号、角色、业务行或数据库结构，未重授锁观测权限。完整安全管理发布、初始化、独立宿主RR、跨端和同条件性能仍待完成。

## 2026-10-20 原按钮实际应用与工作台revision回归

原三按钮已通过真实原前端→OAuth绑定助手→固定C#CLI对独占旧版工程实际应用，主组三请求/一个应用组，26文件写入、0删除，原CLI严格证明事务committed；四保护文件前后摘要相同。第一条完成提示因并发覆盖未被主组DOM记录，后续单独新增一条原配置按钮NoOp补验，545文件摘要保持、同计划再预览0变更、无新增journal，真实完成提示已截图。独立漂移目标被拒且后来编辑保留。本轮grant与网页会话均通过原入口撤销/退出；仅本轮两个服务和Node轮询停止，四封存服务未动。冻结组合及全部证据边界见[原按钮浏览器验收](testing/online-helper-browser-acceptance.md#2026-10-20-原三按钮真实浏览器与本机文件闭环)；不是三提示各自独立截图、迟到身份切换或整个项目完成声明。

5565098的PR/push run `39447794060`、`39447784145` 最终前端2成功、后端4失败；四后端均为同一个Agent反射夹具错误，179通过/1失败/25跳过/205总计，`RolePermissionRevisionCompatibilityTests`调用旧四参数RealmCatalog构造，而新展示字段为第五参数。未执行的后续工程不能算通过，c4六绿不移用于此冻结。修复仅补测试夹具真实构造，并追加展示表名变更不影响原JSON revision的断言，未改变生产revision或权限规则。

本机完整Agent离线结果180通过/0失败/25跳过/205总计，TRX `.local/agent-revision-fixture-fix/agent-revision-fixture-fix.trx` SHA256 `f42d4bad81aa72d3242dec3d4e17ef9700e83795945b49acf484e08b1a09bfff`。再对已钉住的Server-200测试库独占运行全部非RR Agent，实际202/202、零失败/零跳过，含22个真实MySQL场景，其余为离线合同；没有将202全称为实库，未运行3个RR或增加锁观测权限。`.local/agent-workbench-nonrr-current/agent-workbench-nonrr-current.trx` SHA256 `a3789a9e2664700a067ba3cceddee46760dc8f46e40477b9f70500e77b578da8`。

测试后的独立只读实例pin与精确回查，按本轮三份完成收据核52行ID/随机名称含墓碑均无残留；`.local/agent-workbench-nonrr-current/cleanup-verification.json` SHA256 `81140263b6e56e08607e754a1f1699ac2041f961a4e080e727cb2ece0491ce4e`。随后三张performance_schema表仅指定列WHERE1=0检查全部1142，实时确认观测权限仍不可读，本轮未GRANT/REVOKE；`observation-proof.json` SHA256 `94f6ce9a48d7ab216d88f89e0002109b327ca141dd25e3f8a09c7fc98a06eef1`。新提交的远端CI需另收终，不更改556失败历史。

## 可信工作台原接口模板准备

在af13847之后，可信安全管理模块生成原权限工作台三个Controller接口，普通模型不下发；保留原HTTP路由、R封装、字符串角色ID、revision及grants。原身份和正式Web页授权必须同Scoped实例，普通编辑者/同后台super、跨后台拒绝、共享事务和版本冲突保持。没有改前端，也没有放开默认Full、BackendOnly、Runtime A或Agent。

本机实际生成并编译适配，执行真实Web授权类、共享Business和协调器，ADO/ORM传输为离线夹具。最终组合155/155、零失败/零跳过，含20个新工作台用例及partial、候选、生成、凭据和模块相邻合同；不是HTTP/实库批次。14项正式Web DI及更新协调另补验全过：首次筛选器误把两个文件名当类名未匹配，核TRX后按实际WebCoordination_方法前缀补跑，不计零匹配或累计中间15/18/150项。原始报告为 `.local/trusted-workbench-neighbors-v3/trusted-workbench-neighbors-v3.trx`（SHA256 `2f3f53c569d2feda3231603049a389f9ea7269c38821097a66375588e900c9a7`）及 `.local/trusted-workbench-host-coordination/trusted-workbench-host-coordination.trx`；详细现状见[固定管理模块](architecture/trusted-security-management-module.md#原权限工作台三个接口适配)。正式完整宿主、原页面下发及初始化仍待完成。

## 2026-10-21 原工作台资产保真下发

工作台页面、两个原组件及共享状态模块直接作为固定编译资源从现有前端源文件嵌入，只在可信生成入口输出；未修改原前端文件或复写新页面。实际生成输出逐文件与原源码规范LF后的字节一致，重复生成全部内容稳定；普通入口不增加资产，完整管理包仍保留原发布门禁。

主组合101/101、零失败/零跳过（含21个工作台用例），另真实CLI和普通前端打包20/20通过；两个批次不累计重叠次数。报告分别为 `.local/trusted-workbench-original-assets/trusted-workbench-original-assets.trx`、`.local/trusted-workbench-assets-package-regression/trusted-workbench-assets-package-regression.trx`，前者SHA256 `2a98caf4d06606d98cb0ed372cbe890f2c3f450c646cd7a6ba84a79279eeee1d`。本批没有HBuilderX、HTTP、MySQL或浏览器实测，完整管理包自定义路由、Runtime A、初始化仍待完成；来源与边界见[固定管理模块](architecture/trusted-security-management-module.md#原工作台前端资产下发)。

## 2026-10-21 原初始化配置字段源头恢复

原页面、只读YZP MCP和仓库原source均核对到后台类型密码页字段 `1696799603148693507`；缺口来自builder按password名称泛过滤。已只恢复该原声明与空初值，固定三页由62增至63字段；此前62是删减后的仓库快照，不代表完整原界面。四个原ID别名登记、现有ID值不变，物理模型不变。按C#正常入口再生成平台DTO/VO/Service/Controller及前端写入白名单，初始化配置字段不再被请求构造丢弃。没有修改原页面文件或在线元数据，也未执行初始化。

元数据builder先8过/2失败，修复后27/27和files/security/management三个check通过。新生成组合V1夹具读取不存在属性导致编译失败；V2实际104过/2失败发现原50字符限制缺后端校验，生成源按元数据补齐后V3为106/106、零失败/零跳过。报告 `.local/original-admin-password-field/generation/original-admin-password-field-v3.trx` SHA256 `9daa0d97f979fac5c40f6ca3a69462a302d8f4b0bba79213cc3f952e0fd36df3`，原编译错误与V2失败保留。前端261/261；API44过/28跳过/72总计，元数据144过/22跳过/166总计，无失败、缺实库配置的跳过不算已验。

实际生成175个归属文件、0个新建手写文件，元数据SHA256 `651b36ddc4348e7f25501ec3c563f2761c63ceb81e9f51477a0786b11af6765d`；当前源码重复生成后199个现存文件字节稳定。私有 `.local/original-admin-password-field/integrated-receipt.json` SHA256 `bb107c13402556f2f066ba0c1757f27e33132ebf1c19d4ac1ec4744f45c89cc4` 分列全部失败/通过/跳过和边界。配置密码沿原输入/回读语义，登录账号摘要仍不回显。完整管理包、Runtime A及原初始化账号功能仍待闭环。

## 2026-10-21 非冗余物理文件与原文件操作

本轮保持原页面和控件，恢复含固定联接读取的非冗余表物理FileLibrary字段；存储仍为JSON文本，DTO/VO回显数组/null。生成Service通过私有入口保留import/copy原动作，文件引用与业务写入同一事务。保护文件为非所有者补上当前页查询权、真实有效引用、记录文件值及父归属核验，解析与流读取共用；既有所有者路径保持，原V8全部状态/后台super/目录策略仍待对齐，详见[文件验收边界](nonredundant-files.md)。

完整生成器943通过/16跳过/959总计，完整Files模块59通过/17跳过/76总计，API44通过/31跳过/75总计，均零失败；三份报告分别为私有`.local/nonredundant-files/all-generation.trx`、`files-module-first.trx`和`api.trx`。缺少专用实库配置的跳过不计已验收，不累计重叠小筛选次数。正式CLI连续两轮生成，平台199/演示16个文件字节稳定，前端及提交的生成产物均无差异。

Server-200四个专属随机空库、一个受限临时账号下三项实际MySQL测试3/3、零跳过：平台、原文件完整包、非冗余正式完整包。后者经Production/Web原认证，实测上传完成、原生数组/长ID回显、省略/清空/null、复制/删除、仅导入页新增、跨页删除引用、第二行错误整批回滚、XLSX、query-only真实引用读取及撤权；字段清空、坏父及软删记录均拒读。最终运行`eb58c60fe8d043b5`四库/账号均按实例租约清理，独立反查不存在，临时settings已移除。前两次`0950f93cc0184311`和`d4901ed183aa4b36`均为2通过/1失败，分别暴露导入误要求普通更新权、普通查询者缺引用读取路径，已修复；旧TRX及清理回执保留，不改写为成功。总收据`.local/nonredundant-files/integrated-receipt.json` SHA256 `c07d5a413b102efea3d4013ca95509151d86792bca3a44a85b282268a4b0c75b`。

此前a1ddd提交PR CI前端通过，Windows后端938通过/1失败/16跳过，Linux后端937通过/1失败/17跳过；二者同为缺失联接拒绝用例的旧错误文案断言，本轮已修正且完整回归通过，新提交CI另收终。本轮不构成浏览器、真机、同条件性能、多数据库、初始化或完整管理包的完成声明。

## 2026-10-21 原文件状态、首次绑定历史及事务可见性

对照只读原V8，ready保护文件上传者不再凭所有权永久读取：普通身份需当前真实引用及页/记录权限；uploaded私有草稿仍可由上传者读取，PUBLIC必须ready。当前后台super由真实Web身份同项目/后台证明，独立包另绑定已发布目录，不等同平台管理员；旧授权器缺省拒绝此特权。上传者仅在从未有引用时免查询权首次保存，含软删引用的历史用同事务当前锁读，解绑不恢复例外；确有可读引用的他人文件可按原规则绑定。没有改原页面、组件、接口字段或物理表。更新Runtime及独立Host要求见[原文件权限](original-file-access-policy.md)，管理动作/目录/复杂精确数据管理和云存储仍待独立验收。

真实文件套件首轮75通过/1失败，旧子页用例错误期待历史解绑后可重新绑定；按原V8改为拒绝/回滚断言，并用新文件继续软删，第二轮76/76。补查同批复用文件再现另开连接看不到本事务新父/新业务行的缺口，定向反例0通过/1失败；改为在观察器内复用实际连接/事务，不处置外部连接，最终完整Files 77/77、零跳过，其中18个真实MySQL及59个离线用例，不把77全部称实库。真实固定完整包宿主组合重跑102/102、零跳过；API重跑51通过/31跳过/82总计、零失败。四轮Files报告及首因均保留，私有路径为`.local/original-file-state`。

正式空库最终`bfefc22467714673`三项3/3、零跳过，含普通Web上传者草稿读取、ready无引用拒读、首次保存例外、缺查询权拒读/拒历史绑定、恢复查询、历史解绑拒重绑、合法引用他人文件、后台super下一请求撤销/恢复，以及普通身份同次导入两行复用新文件。四个随机库/一个受限临时账号已清理并独立反查不存在，临时settings已移除。此前`5b7fb150e33f42be`3/3及其清理记录仍保留，不以旧批替代新增同批导入。四轮文件套件的Server-200独立只读核验：测试表前缀无表、本轮时间窗worker851的文件/引用/目录（含墓碑）均0，未更改观测权限；证据为`files-cleanup-verification-v3.json`。

最终正常生成核对曾第一轮成功、第二轮平台报Access denied；生成及前端diff为空、无其他测试进程，独立原生成器诊断随后两轮成功，正常CLI再两轮成功，平台199/演示16个文件字节稳定。原失败`regeneration-access-denied.json`保留，原因尚未证明，不宣称已修复；没有为此改生成器或丢弃文件。总收据`integrated-receipt.json` SHA256 `c4e768ad777983de762dbe277a2b16e2d5d8b3accf42109045c532f080b86265`。

此前981c85a的PR与push两组前端/Linux/Windows共六项CI已全部通过；本批新提交另收终。浏览器、真机、同条件性能、多数据库、完整管理/初始化及其余原目标继续保留，不因本批通过宣称全部重写完成。

## 2026-10-21 原文件库浏览、共享目录与管理接口

继续适配C#后端，未修改原前端页面、样式、交互、生成模板或物理表。目录恢复完整项目共享树，JSON使用原parentId/id/fileCount，列表/完成对象补回id并保留fileId；无请求体目录查询、原返回对象/成功字符串、共享目录整理、同级判重和拒环按原V8对齐。文件列表按真实字段模式及读取权筛选，空关键词按目录、分词字符有序LIKE跨目录，计数复用可见规则。重命名/移动他人可读文件不改变已验证MIME、存储键和内容；删除仍限定上传者或当前超级身份且无有效引用。详见[原文件管理规则](original-file-management.md)。

完整Files最终83/83、零失败/零跳过，23个真实MySQL与60个离线用例；Windows实际占用对象的Controller调用确认物理删除失败返回R错误，数据库已失效但物理对象仍可残留，没有自动重试/文件系统事务保证。最初两项真实反例0通过/2失败；79项初验通过后，扩展套件82通过/1失败为测试错误构造双项目单库解析器，修正后占用单项及完整83项通过，旧报告保留。所有文件套件测试表及本轮时间窗worker851的文件/引用/目录含墓碑经Server-200独立只读确认均0，未变更锁观测权限。

正式空库最终3/3、零跳过，覆盖平台、原文件包、非冗余正式完整包，新增原HTTP目录共享、字符串长ID、重名/拒环后未改写、搜索/计数、他人可读文件整理、MIME/摘要/内容不变、删除拒权/拒引用/拒非空及后台super删除无引用文件。前次2通过/1失败为新验收误设业务状态拒绝HTTP400，完整包既有契约为500；保留既有映射并增加拒绝后未写入核验，未通过宽泛异常透传来适配测试。两轮各四个随机库/一个受限临时账号均按租约清理、独立反查不存在，临时settings移除。

完整包/授权/更新器组合最终176/176、零跳过，含旧包新增运行时文件升级、未知多点源码拒绝、手写文件保护与恢复。最初102项组合100通过/2失败，发现新浏览partial未入固定更新路径；随后编辑脚本未匹配而未写入，扩大回归123通过/52失败仍为旧名单相关，记录保留。精确登记FileLibraryService.Browsing.cs为sync-runtime后最终176项通过，没有放宽任意路径。API51通过/31跳过/82总计、零失败，跳过按逐项NotExecuted核算，不能使用TRX中异常为0的汇总notExecuted值。

正常CLI连续两轮生成，平台199/演示16个文件字节稳定；前端、模板及生成产物diff为空。私有报告在`.local/original-file-management`，总收据SHA256 `b7339372f0053b652938f3e9e169d08876843b61600ffe2d930f342c63b3964b`。前一ebece61的PR/push六项CI已另行核实成功；本批新提交CI另收终，旧CI不替代新结果。

另按用户本轮明确授权，重新核对原审批结构与逐字段摘要后，仅清理六张既有失败测试表，两组各11/4/4行；独立连接逐表确认不存在，并另作Server-200只读复核。原失败状态和证据保持，不改写为测试通过。该授权不扩展到其它表或既有业务数据。清理收据保留在原失败样本目录及RecoverFailedSamples审计目录。

浏览器、真机、完整云存储、复杂精确数据管理、完整安全管理/初始化、同条件性能、多数据库及其余原目标仍待完成；本批不构成整个重写项目完成声明。

## 2026-10-21 精确管理直接动作决策基础

继续原V8精确数据管理对齐，发现原角色聚合只保留页面动作而丢失is_scope授权来源。本批保留已经支持的直接页面/当前动作决策：实际有效授权全部来自范围角色才标记精确管理，is_all作为当前动作的另一条来源；任一普通角色实际授予该动作可解除该动作限制，其他页、动作、菜单、损坏或已删授权不影响。后台super保持原不受此限制的语义。普通认证和同事务锁后复核均带回最新集合，旧位置构造不变、JsonIgnore保持原HTTP/会话协议。本批没有改前端、模板、元数据或结构。

共享Security31/31、独立完整包真实授权类94/94均为离线合同，零失败/零跳过。新增两项通过真实认证代码及离线SQL传输核验角色变化下一次认证生效、旧身份快照不被原地修改，以及混合角色只解除实际授予的动作，不能称作实库。非RR Agent完整228/228、零失败/零跳过，其中22项真实MySQL、206项离线；第一轮225通过/3失败为命令漏传既有专项实例pin，三个场景在连接/创建夹具前拒绝，补回既有pin后完整重跑通过，原报告保留。未执行RR三项或重授观测权限。

Server-200独立只读核对两轮报告时间窗、实际固定节点853/855/856/881，账号/角色/权限/后台/会话/任务/grant及演示业务ID和创建人范围含墓碑均0；不是泛删或读取业务正文。私有报告在`.local/exact-data-manage`，收据SHA256 `d130f04c3461d8b0bed13f923c0fb55402f5ffb5a9a6056435f2b3d93a7fded8`。

这些决策尚未消费为记录SQL，不能据此宣称范围角色已经安全开放使用或精确管理完成。页面/账号匹配字段编译、单值/数组/空值、父子继承来源、查询/导出/写入各自动作、文件读取附加条件及正式完整包实库范围验收全部继续，详见[完整精确管理链路](exact-data-management.md)。原MySQL完整目标、浏览器/真机、性能及多数据库范围不缩减。

## 2026-10-21 构建子进程测试隔离

340e55c的[Windows PR CI](https://github.com/zhao-yc/metarill/actions/runs/39537647405)实际失败，Generation为950通过/1失败/16跳过/967总计；首因为SharedSourceBuildGateTests等待子进程ready握手超时，不是元数据负向用例预期打印的错误。Linux及前端成功不替代Windows结果。本机原定向3/3通过，原CI超时证据保留，尚不能证明原延迟的唯一原因。

进程生命周期三项改入禁止并行的独立xUnit集合，避免与大量独立包编译并发干扰门闩顺序断言；生产SharedSourceBuildGate、超时、停止/排空及释放顺序均保持。隔离后的生命周期与实际独立包构建相邻组合11/11、零失败/零跳过，报告与收据保留于私有验收目录，收据SHA256 `758fcc3867f7d84b650af6db4160bb9d6fb0e35a68bb2618a9073038c6382ce7`。这不代替新提交Windows完整CI，修复后全套结果仍待收终，不通过重跑旧失败将其改记为成功。

## 直接页物理字段的精确范围执行链（2026-10-21）

本阶段将逐动作范围角色决策接入真实账号窄值、已发布后台字段、生成查询/写前锁定、导出/覆盖导入及文件真实引用。前端页面、样式、交互、接口和平台元数据未改动；没有 SQL 或物理结构迁移。派生匹配字段及继承组合仍待完成，详见[精确数据管理对齐](exact-data-management.md)。

本次协调者顺序实际执行：安全 56/56、文件 83/83、Agent 非 RR 228/228、生成精确范围专项 7/7、独立正式宿主/非冗余完整包/本机更新组合 169/169，全部零失败、零跳过。Agent 显式排除原 RR 专项，未重新授予锁观测权限。文件和 Agent 样本另外完成独立只读残留核验。

正式空库三项另行实际 3/3、零跳过，使用 Production/Web 真身份与真实数据库；范围内修改及覆盖成功、范围外拒绝和原追加规则均得到实测。四个随机库及临时账号完成精确清理、独立确认不存在，私有凭据文件已移除。之前写后回显、详情测试契约及 SQL 参数绑定的失败记录全部保留，不将原失败改记通过。

最后通过正式开发脚本连续生成两轮：平台 199 文件、示例 16 文件与原产物及两轮之间逐字一致，24 个手写 partial 摘要保持；前端、平台元数据和原生成目录没有 Git 变化。本批未重跑浏览器、真机或性能，不以 SQL、HTTP 或包构建替代这些验收，也不声明 MySQL 首版全部完成。代码保持开发分支及草稿 PR，不合并 main 或部署生产。

## 资源权限继承与独立文件来源（2026-10-21）

本阶段对照原资源权限分支，按已发布及当前有效元数据合并直接、真实父子、选择引用来源，保持原页面、样式和操作。子页继承同类动作，选择页只继承查询，不做传递继承；文件排除选择引用来源，单独保存查询与精确范围决策。继承 is_all 只进入复制/导入的内部来源，不绕过父页关闭的标准动作。

协调者顺序实际验证：安全 98/98、正式宿主及完整非冗余包 102/102、原文件回归 83/83、Agent 非 RR 229/229、已有工程更新 67/67，均零失败/零跳过。当前目录 22 项定向已包括在 Agent 全量中，不另行累计；新增 Reader 用例实际验证当前重绑撤销旧继承、未发布目标不获权、恢复后重新生效，同引用缓存与 Session/事务证明保持。

正式空库三项另行实际 3/3、零跳过，Production/Web 不注入固定授权器。真实选择引用使目标列表、详情、原候选查询可用，父范围仍准确；没有目标独立文件授权时文件拒绝。直接范围、普通父子来源、错误动作、撤权与账号匹配值切换均用实际上传、原 Service 引用、二进制流、resolve 和原文件列表核验。关闭父修改后继承 is_all 的更新实际拒绝且原行不变。四个随机新库及临时账号精确清理、独立确认不存在，私有凭据移除。

保留首轮失败：夹具将选择列表作为内嵌子表，生成即拒绝，未执行该轮 Web；随后使用同表独立选择页/普通子页，通过原写入切换真实引用。该场景不代表所有内嵌列表类型支持。文件回归初次 82/83 的失败发生在夹具物理清理死锁；经逐字段与二进制签名核对恢复本轮唯一空表和软删测试目录，共用数据库夹具改为独占集合，清理失败保留无凭据恢复清单，未添加生产自动重试。Agent 初次 227/228 保留，更新四表旧断言并增加当前重绑验证后全量通过。所有失败原始记录保留，最后独立只读 15 项残留统计均零，未新增观测权限。

两轮正式生成与原产物逐字一致：平台 199、示例 16，24 个手写 partial 不变。前端、平台元数据与现有生成目录没有 Git 变化，无 SQL 迁移。此前提交 3573d9c 的 PR/push Linux、Windows、前端共六项 CI 已全部成功，该结果不自动覆盖本次继承代码。

本批没有重跑新锁集合的 RR、浏览器、真机或同条件性能；夹具隔离不证明生产并发。派生匹配字段、复杂 Where、完整控件/布局/并发组合及 MySQL 首版整体验收继续保留。代码仍提交到既定开发分支，草稿 PR 不合并 main、不部署生产。

## 已登记单值派生字段的精确范围（2026-10-21）

继续适配 C# 后端与生成源头，原前端、样式、交互、接口、平台元数据及物理结构未改动。固定匹配计划验证真实单值来源、统一 int64 关联键及项目列，读取在分页/count/统计/导出前过滤，写入在原事务中分别锁主记录与关联路径。文件使用启动编译的窄投影，再核对真实引用、实际文件值和父范围。完整边界见[精确管理说明](exact-data-management.md)。

本批顺序实测：固定读取 38/38（含一项真实 MySQL，覆盖双连接单级/两级关联锁及 RR 当前读）、权限 98/98、文件 83/83（23 项真实 MySQL）、生成与完整非冗余包 16/16，均零失败、零跳过。最初文件回归因命令漏传平台元数据为 60 通过/23 跳过，补回配置后重跑全部执行；原跳过报告保留。生成专项包含实际生成程序集的隐藏依赖及独立锁计划，完整包包含原更新预览/应用、重复稳定及手写保护。

最终正式空库三项 3/3、零跳过，Production/Web 不注入固定授权器。验证原路由下派生范围内写入、范围外拒绝、复制后删除、覆盖导入、实际 XLSX 和文件流/resolve/list；目标值变化、软删/跨项目、账号数组/空值和撤权逐请求生效。之前一轮也为 3/3；最终代码另外完整复验，不能套用旧批。三轮各四库和受限临时账号均清理、独立确认不存在，私有配置移除。

首轮适配层 26 通过/1 失败为 FreeSql 更名后已有 lambda 仍引用旧别名，修正为维持生成 Mapper 的固定别名后通过；首轮正式包 2 通过/1 失败为新夹具 JSON 转义数组超过既有账号字段长度，缩短样本值后通过，未扩大字段。原失败及清理证据均保留。

独立只读核验：本轮时间窗 worker851 的文件/引用/目录含墓碑、临时文件表及三轮六张固定读取样本表均零残留，没有授予观测权限。正式开发脚本重复生成两轮：平台 199、演示 16 文件与原产物及两轮逐字一致，24 个手写 partial 不变；前端、平台元数据和现有生成产物 diff 为空。私有报告在 .local/exact-data-manage，正式空库最终编号 950b2484acae4f3b。

此前 808678f 的 PR/push 两组 Linux、Windows、前端共六项 CI 已核实通过，不替代本批新提交 CI。新七表安全目录 RR、复杂 Where、多值/其它派生和控件完整组合、浏览器、真机、同条件性能及后续数据库继续保留，未宣称整个 MySQL 首版或重写项目完成。仍在既定开发分支及草稿 PR，不合并 main、不部署生产。

## 固定条件基础与数值误匹配修正（2026-10-21）

对照只读原 V8 的结构化候选编译，新增冻结的条件计划与按逻辑类型参数化的适配层，保留 LIKE、IN 和 NULL 语义。当前尚未接入正式候选、写入、导入依赖或文件范围；原生成拒绝检查保持，不将这部分基础宣称为复杂 WhereSql 已支持。剩余完整接线见[条件计划](fixed-candidate-conditions.md)。

五个离线反例真实复现整数/decimal 舍入和 double 下溢误匹配，原报告 0 通过/5 失败保留；修复共用转换后同时保护既有精确范围。绑定首轮 26 通过/1 失败是坏参数名的旧标识符异常类型断言，修正测试后通过。一次编辑脚本引号冲突在 Python 解析前失败，没有修改文件或执行测试。

本轮顺序验证：元数据/数值专项 64/64、数据专项 70/70（含自有随机表真实 MySQL）、权限 98/98、文件 83/83（23 项真实 MySQL）、生成及完整包 16/16，均零失败/零跳过。中文注释补全后额外复核相关元数据 64/64、条件绑定 32/32，属于重叠回归，不累加。

正式空库三项实际 3/3、零跳过，使用 Production/Web 真实身份，新增数字派生范围的合法读写、舍入/下溢错误拒绝、原状态未改及保护文件流/resolve/list。四库及受限临时账号已清理并独立反查不存在，私有配置删除。文件/引用/目录含墓碑、临时文件表及本轮两组四张读取样本表独立只读核验均零，没有新增观测权限。

两轮正常生成仍为平台 199、演示 16 文件与原产物及两轮逐字一致，24 个手写 partial 不变；原前端、平台元数据和生成产物 diff 为空。私有报告在 .local/fixed-conditions，正式空库编号 4deac809469a4b21。此前 24bf2dc 的 PR/push 六项 CI 全部通过，不覆盖本轮新代码。整个 MySQL 首版、复杂条件完整接线、原浏览器、并发/跨端/性能和后续数据库继续保留，仍不合并 main 或部署生产。

## List 常量条件接通生成 Service（2026-10-21）

原 List 字段及页级覆盖的物理标量常量条件已接入原数组候选、分页/count、已选回读、真实实体新增/明确更新、默认值、手写钩子、复制和导入。候选、外键及显示目标必须一致；表单/账号扩展变量、页面级复杂 WhereSql、单选/多选附加条件继续明确拒绝。原前端及交互、平台配置和数据库结构未变；没有用此项替代整个平台完工。

正式生成并编译的 HTTP/MySQL 用例覆盖合法/越界候选、旧只读回读、关键词/count、客户端身份伪造不生效、默认/钩子拒绝、更新与复制、导入整批回滚及独立目标父乱序解析。只修改目标父而省略关联外键时也重验。写入沿用当前页动作，不新增目标列表权限。

新增 RR 反例先真实失败：已有旧快照的 Count 预检误拒绝刚变为合法的候选。改为同事务窄 FOR UPDATE 当前读，并当前锁定真实父。随后验证候选当前值、父删除拒绝、两连接目标/父锁等待、无关目标可写及提交释放，未授予观测权限。失败报告与成功报告分别保留，不能把旧失败改写为通过。

相关回归首批 126 通过、2 失败、0 跳过；两项失败因未传入实例 pin，在创建样本前拦截，补齐验收环境后原两项重跑 2/2。范围包含 5 项真实 HTTP/MySQL 夹具，其余为编译、目录及独立包/更新验证。补充最终目标一致性与条件专项 13/13，包含重叠用例，不与前述结果累加。首轮验收代码原始字符串及局部命名编译错误、一次误用根写路由的失败都未当成通过。

本轮没有重跑 Production 空库登录/文件、原浏览器、真机或性能用例，不能复用上一批回执为新验证。List 常量通过不表示 formParameters、完整导入依赖、页面/文件扩展条件或完整控件组合已经完成。正式源码生成保持平台 199、演示 16 文件两轮与原产物逐字一致、24 个手写 partial 不变；前端、平台元数据和现有生成物 diff 为空。

本轮常量关系与非冗余随机样本均清理并独立反查。用户授权的原六张失败测试表也再次通过只读独立连接确认不存在，没有重复执行 DROP。私有原始 TRX、窄并发和清理回执在 .local/fixed-conditions；代码仍在既定开发分支与草稿 PR，不合并 main 或部署生产。跨端、同条件性能、完整 MySQL 首版及后续数据库仍按原计划继续。

此前 eb22798 的 PR/push 两组 Linux、Windows、前端共六项 CI 已逐项核实通过；本批次新提交的 CI 单独记录，不能沿用该结论。

## 本对象表单联动条件与实际保存一致性（2026-10-21）

原List候选已接通本页开放物理标量的平铺及表名嵌套formParameters，同一计划覆盖旧数组、分页/count、已选回读、实际实体新增/更新、默认/手写规则、复制、导入和导出。声明路径优先，只有缺失才使用同字段别名；null、重复键、数组、类型及源数值范围严格处理，身份仍由服务器建立。原前端、界面流程和前端生成模板未改，平台元数据及现有业务库无SQL迁移。

完整导入依赖拓扑包含父及全部联动字段，保持首列覆盖、锁定原记录的省略值和整批回滚。任一依赖变化，即使FK省略也重验。导出按实际父和联动值隔离名称缓存，额外依赖不加入表头。非冗余页级where覆盖仅在完整候选计划已证明、目标/名称和其它定义保持一致时开放，其余门禁保留。

真实反例先复现了更新钩子漏登记字段仍返回成功：验证使用了未持久化的新值，数据库保留旧值。新增窄原值快照，关联、依赖及目标父发生未登记变化时拒绝更新；正确登记的规则仍可保存。冗余与非冗余源表都验证了两种漏登记路径、合法登记、原槽、默认/钩子、复制、乱序/覆盖导入、真实XLSX、RR当前读及两连接锁释放。本轮HTTP夹具使用固定服务器身份，不能当成浏览器或Production登录下全部联动已经验收。

最新生成链路回归141/141、条件/表单与数值合同87/87、数据项目147/147，均零失败/零跳过。包内restore/build、原助手预览/应用/重复应用不变和手写partial保护包含在生成回归中，不另累加。此前数据全量146通过/1跳过为未提供平台元数据路径，补齐后单项反查通过，最终147项全部重跑通过。槽兼容、独立包及两个实际联动专项与整批重叠，不累加。

失败原件保留：完整包首轮因夹具缺少List目标页及独立父筛选声明而拒绝；非冗余首轮被尚未接线的页级覆盖门禁拒绝，随后增加完整候选证明；漏登记钩子反例初次为0通过/1失败，修复后两个存储模式及整批回归通过。没有通过删除目标/名称门禁或改写失败记录收口。

最终Production/Web空库3/3、零跳过（6e42ea4cdea0471c），验证正式宿主登录、生成CRUD/范围、原导入导出及文件链路回归；本轮前两批同为3/3，保留各自证据。三批各四个随机库及受限临时账号均清理并独立核验不存在，临时凭据文件移除。最新两轮正常生成仍为平台199、演示16文件与原产物逐字一致，24个手写partial不变，原前端、前端模板、平台元数据和现有生成产物diff为空。

按本轮精确所有权记录再次独立只读反查70张样本表均无残留，没有新增观测授权或操作既有业务表。报告在.local/form-conditions，原失败与最终通过分开存放。此前a4ad620的PR/push共六项CI已全部通过，本批新提交另验。

跨对象/父对象及账号扩展变量、页面级扩展WhereSql、单选/多选附加条件和完整控件组合、原浏览器、跨端、同条件性能、MySQL完整首版与后续数据库仍继续实施。代码保持既定开发分支与草稿PR，不合并main、不部署生产。

## 原账号写入与服务器退出补验（2026-10-21）

本批真实 Ego Lite + HBuilderX 原页面完成一个随机普通角色和账号的新增、角色勾选、改密、持久回读、旧密码拒绝/新密码登录以及原确认逻辑删除。独立数据库确认既有账号/角色完整行摘要未变，测试账号和角色活动记录为零；工作区未分配设备的边界明确保留。详见[原账号浏览器验收](testing/original-account-browser-acceptance.md)。

终审实际发现原平台仅清理浏览器缓存、未调用 C# 注销的缺口。平台及独立前端模板已补服务器注销和身份时效守卫；重新发行后，原按钮退出前同一令牌有效、退出后返回“登录超时”，独立 SQL 证明该会话已逻辑注销。旧失败留下的一条自有测试账号会话另行精确逻辑撤销，不能将此清理算作浏览器修复证据，也不声称全部历史操作者会话均已清理。

完整前端273/273（含新增12项）、独立前端打包与原壳生命周期19/19均零失败零跳过；API构建0警告0错误。正式生成两轮平台199/示例16项逐字一致，24手写partial保持。前序1684cb9的PR与push两组Linux/Windows/frontend共六作业已实际全部成功；本轮提交的新CI须另核。数据库、元数据、原页面/样式和标准页面模板无结构变化，无生产部署。独立完整安全宿主、其余业务、跨端和同条件性能仍未完成。

## 可信生成管理HTTP并发补验（2026-10-21）

真实持久普通Web身份、生成三管理页Controller/partial和同一共享协调器的原回归与三项并发最终4/4零跳过通过：旧RR身份快照后原工作台撤权，生成写入拒绝；实际生成账号update迁后台后旧请求冲突，回滚前不补新后台锁且不覆盖合法提交；无关后台账号/角色/授权持锁期间，工作台保存及生成角色删除完成。探针先验证自有精确主键存在，再用独立UPDATE锁超时/成功和回滚后释放作证，不读取观测表或申请权限。

第一批2通过/2失败和诊断0通过/1失败保留，首因是测试将驱动Unspecified误当非RR；仅改为在同一事务查MySQL实际隔离并严格要求RR，未改产品事务或延长超时。随后两场景2/2及完整4/4分别保留，不重复累计。所有11轮自有项目包含失败批次，独立连接对六表66个范围核零残留。完整方法和边界见[可信管理实库专项](testing/trusted-management-runtime-mysql.md)。

本批只修改验收夹具与文档，未改生成产物、元数据、模板或原前端；不需要SQL、再生成或运行时同步。默认完整Program、初始化行为确认/跨库入口、独立安全管理原页面、其余业务与并发、真机、同条件性能及多数据库仍未完成，不解除完整管理包门槛、不合并main或生产部署。


## 页面固定标量条件（2026-10-22）

原页面WhereSql的物理标量常量已接入生成期类型校验、静态不可变计划和参数绑定；项目/父身份来源、原接口及前端保持。冗余与非冗余的page/count/detail/选中回读/统计/XLSX消费同一范围，候选、关系写入及名称导入采用目标页实际条件。写前定位仍受页面限制；保存后允许新状态离开页面，私有事务回执保留ID/项目/父范围。隐藏条件依赖不进入VO或Excel，来源额外条件未被目标执行仍拒绝。详见[页面固定条件](fixed-page-conditions.md)。

真实MySQL/生成HTTP两个存储模式最终通过，覆盖原LIKE与NULL三值语义、高位ID、范围拒绝、保存后离页、新增、复制/删除、范围内覆盖后离页、范围外同名不覆盖、候选名称及整批回滚，实际下载并读取XLSX。固定测试授权器不代表完整Program、原登录或浏览器。新多选范围正反例执行实际生成编译，不冒充多选全部并发组合验收。

数据适配全量149/149零跳过。较宽生成回归首次为236通过、4失败、1专属空库跳过；失败分别为旧多选提示断言与3项缺少验收仓库环境变量，原件保留。补明确目标范围拒绝断言及环境后，定向33/33零跳过，包括全部失败场景、两个实库模式及新多选证明；专项与较宽回归有重叠，不累加为全量一次通过。完整包有/无文件与有/无页面条件四种组合均通过包内restore/build、原Vue协议及受管更新/手写保护，包含在上述较宽回归中。

正式Production/Web空库基线另由专属脚本完成3/3零跳过（788852ea4c854985）；四个随机库及临时账号均独立核验清理，临时凭据文件已删除。页面专项8轮共24张随机表，包括首轮失败样本，已独立只读反查不存在。首轮另发现非冗余count漏传条件并修复，保留首轮2失败与后续2通过及最终33通过的原始证据。

两轮正式生成平台199、示例16项均与修改前逐字节一致，24手写partial不变；原前端、前端模板、平台元数据及现有生成产物diff为空。86679c6的PR与push两组共六项CI已实际成功，本提交CI另核。没有DDL、回填或界面变化；已有项目需配套生成Service与Data运行时的源码更新，不能只混换DLL。

OR、函数、派生/数组/凭据条件、新增账号/跨对象/表单变量及未证明的控件附加条件仍拒绝。初始化行为确认、完整安全宿主和其余业务/并发、原浏览器复杂条件、跨端、同条件性能、完整MySQL首版与后续数据库仍继续实施，不解除完整门槛、不合并main或生产部署。


## 文件来源承接页面固定条件（2026-10-22）

原文件范围正则仅接受身份/父等值，不能消费已支持的页面常量；现用同一AST、Core规范节点输出与Data类型参数计划，并在发布快照中缓存。物理文件来源SQL与非冗余派生精确管理SQL追加同一页面条件，隐藏依赖不扩充文件响应，坏来源不回退宽查询。写观察者保留真实父锁、首次绑定及文件复用规则，不以保存后新值再次强制页面筛选。

Files完整98、固定条件元数据64、Data完整152、生成/独立包116均全部通过、零跳过。原页面与脚本未改，四种独立包文件/条件组合仍通过源码构建、原Vue协议和受管更新保护；新批次之间有重叠，不累加。正式CLI两轮平台199、示例16项逐字节一致，24手写partial不变，原uni-app前端/共享模板目录、平台元数据及现有生成产物无diff。

Production/Web完整非冗余包实际采用固定页面常量：真实super经生成接口保存包含附件的离页记录，独立SQL核对当前记录及页106引用仍在；普通query-only账号的保护内容、resolve与列表均拒绝，恢复测试行后重新可读。物理来源也验证固定条件不匹配时拒权。此处使用正式登录与持久身份，详细证据与尚未覆盖的普通编辑者复用/并发组合见[文件来源说明](nonredundant-files.md)。

专属空库3/3零跳过（9daebecc40ff4552），四个随机库、临时账号及临时凭据文件清理经独立核验；另只读确认文件测试表前缀无残留，不冒充全部文件记录审计。1eaab18的PR/push共六项CI已实际成功，新提交CI另核。无DDL、生产部署或main合并，完整安全宿主、其余业务及并发、跨端、同条件性能和后续数据库继续实施。


## 可信管理前端装配准备（2026-10-22）

原权限工作台四资源、原路由样式、三个管理页及共享宿主现可在内部工厂签发上下文装入同一只读资源图。项目/命名空间/摘要、菜单树、额外入口及原资源逐字核验；普通同形生成与改写资源拒绝，准备不写应用目录、不解除Runtime A或初始化门槛。公开普通完整打包、非空目录保护与原子发布入口保持。完整边界见[管理模块说明](architecture/trusted-security-management-module.md)。

Node原依赖/路由验证通过；私有资源副本以HBuilderX 5.26 Vue3实际导出Web，入口及原工作台JS/CSS已核验，临时工程已关闭，未托管或启动浏览器。该构建使用用户已提供AppID，产品模板不带标识，Tiny Cloud仍空；不计为完整宿主或真机验收。

普通前端与资源首批修正后25/25；较宽可信模块/工作台/普通完整包/认证/原子发布组合为102通过、1处新测试异常类型断言失败。修正为原校验器实际InvalidOperationException后，8项资源正反例全部通过、零跳过；包含重叠，不累加。首轮Node参数及缺失路由报错顺序失败原件保留，未删除原页面或放宽门槛。

两次正式生成215文件逐字稳定，24个手写partial保持，原前端、模板、平台元数据及生成产物无改动。没有数据库、DDL、初始化、main合并或生产部署；完整宿主集成、初始化行为确认、浏览器业务、跨端、性能和后续数据库继续实施。


## 独立导航恢复原自定义入口（2026-10-22）

独立宿主 Navigation.cs.tpl 现将同项目/后台且已授菜单权的已发布 CUSTOM_PAGE 返回为原 route/pageId，继续由原菜单运行时导航，不虚构 CRUD/count。缺失/隐藏/跨归属目标和非法路径拒绝，原生成页表身份、列表权限与计数保持。真实普通 Web/MySQL 验证页权撤销不等于菜单撤销，工作台仍按原角色页动作403；菜单授权撤销后下一请求隐藏。没有改前端、页面、样式或交互。

最终同进程组合153/153、零失败零跳过，含新增13项、实际模板认证/协调、前端包和真实管理HTTP/并发4项。首组合59过/94失败全部是重复夹具加载同名宿主程序集，修复为既有 partial 测试类共用夹具后完整通过；中间140项不含新增13项，原件保留不累计。原菜单运行时28/28，实际 C# JSON 另由原 Node 菜单运行时验证导航，不称浏览器已通过。

正式两轮生成平台199、示例16文件逐字稳定，24手写 partial 不变；原前端/前端模板/平台元数据/现有生成产物无diff。Production/Web专属空库3/3（ece50f8b3a114b5c），四库和临时账号独立核验清理、凭据文件删除。本批13轮自有管理项目（包含失败批次）另起只读连接检查六表78个范围均零残留，不执行补清理。详见[管理实库说明](testing/trusted-management-runtime-mysql.md)。

原初始化行为确认仍待答复，不注册其DI/HTTP或解除Runtime A；默认完整管理Program、原页面/跨端、同条件性能及后续数据库继续实施。此批无DDL、回填、main合并或生产部署；已有独立包需配套同版宿主受管更新再构建。

前序 a80e0e2 的 PR/push 两组共六项 CI 已实际全部成功；本批提交的 CI 另核，不沿用为新提交通过。


## 可信管理真实 Program 接线（2026-10-22）

正式宿主现通过生成注册类建立物理图。普通包严格合成不变；只有工厂可信编译目录可移除固定三管理页的结构声明，完整发布摘要、物理列/索引和合成结果范围均核对。额外基础对象扩大10表图的纯内存反例已复现并修复。

管理单请求改为实际Production Program，执行原登录表单、R.map令牌、原退出、正式DI/中间件及生成接口；账号、角色、后台配置、导航/工作台、旧revision拒绝、撤权和数据库断言通过。三个RR并发仍为额外事务夹具，不当作默认Program全部并发；内部工程引用当前clone基础项目，不称完整公开管理包。已知YZP_CONFLICT恢复为400与原提示，未知内部异常继续隐藏。

较宽363/363、零跳过；随后物理范围补强的最终可信组合131/131、零跳过。保留首轮Content重复、错误令牌层级、测试辅助方法访问错误和真实旧revision500，以及修正后的各次TRX；不累加重叠结果。最后补强只改变可信分支，普通215项正式产物两轮逐字一致，24手写partial保持；本批正式差异仅两份注册类及其生成归属，原前端/模板/元数据无diff。

全新普通独立应用/平台基线3/3（c568606165bb4122），四库与受限临时账号均独立核验清理，凭据配置删除；最终可信分支补强后普通生成字节保持。12轮自有管理项目六表72个范围独立只读核零，成功Program三轮配置文件均移除。前序110b4bc的PR/push六作业实际成功，新提交CI另核。

不注册待确认的原初始化按钮，不解除默认Full/Runtime A门槛，不合并main或部署生产。既有独立C#应用需配套宿主更新、再生成注册类并构建，无DDL或回填；完整发布、原页面浏览器、跨端、同条件性能与后续数据库继续实施。


## 原管理前端与实际宿主同源联通（2026-10-22）

新增仅显式人工模式启用的有界验收会话和回环静态/API工具，同一冻结目录生成的原前端已通过Node闭包及HBuilderX 5.26 Vue3发行。实际同源HTTP验证原登录、三管理菜单、账号页、工作台读取及原服务器退出；原工作台编译块与头像资源摘要核对。产品页面、模板、元数据和后端运行时未修改，未放宽正式CORS或管理Full门槛。

原Ego空间24已不存在，重建请求待答复，未绕过空间恢复规则。会话以build-only结束，不计页面渲染、表单或工作台交互验收。默认会话边界及真实管理/并发15/15，人工阶段后原HTTP1/1，均零跳过且不相加；私有代理2/2，已纳入后端Linux/Windows CI。首轮辅助根目录未规范化导致路径拒绝，保留失败后仅修正辅助目录。

7轮自有项目包含失败样本，独立只读六表42范围零残留；口令文件、私有前后端和临时HBuilderX工程均清理。完整步骤见[原页面联调入口](testing/trusted-management-browser-entry.md)。浏览器、初始化待确认行为、跨端、同条件性能及后续数据库继续实施，不合并main或部署生产。


## 自关联级联固定读取（2026-10-22）

原级联两个POST地址已由C#生成源接通selfTree候选和路径，保持原请求、R.data、字符串ID与五字段选项；未改前端、组件、样式、页面模板或平台元数据。目标唯一可读页、真实双页list权限、项目/软删/固定条件共用，最小物理投影与参数化逐层读取；断链、循环、深度/单层溢出整体拒绝。完整写入、受管名称、列表展示、后代筛选、导入导出、跨表和其他范围仍未接线，Full门禁保留。详见[级联读取合同](cascade-tree-read-contract.md)。

最终组合49/49、零跳过，含真实生成编译与一个实库HTTP用例的整数/字符串父键两轮；前序专项7/7有重叠不累计。首轮反例夹具失效导致1失败，原件保留。四轮自有两表完成精确清理及独立连接八表核零；没有将专项测试身份当成正式登录或浏览器。补充完整包/能力门槛及页面条件生成回归35/35、零跳过，包含包内基础源码独立构建，不宣称级联完整包。平台199、示例16项两轮正式生成均与原来逐字一致，24手写partial不变；原uni-app前端/共享模板目录、平台元数据及现有生成产物无diff。本批提交CI另核。

用户已授权的旧失败两组三表此前已按原摘要清理，本轮新连接再次核对Server-200实例与六表不存在；未重复DROP，原失败及清理收据保持。历史文档中的“待批六表”由此关闭，原失败测试不改为通过。浏览器空间和初始化确认仍待原问题答复；完整MySQL首版、跨端、同条件性能及后续数据库继续实施，未合并main或生产部署。


## 自关联级联写入与受管名称（2026-10-22）

生成Service在默认值和手写钩子之后，以当前事务ForUpdate校验完整祖先并回填叶子名称；严格DTO不接受名称写入，原完整表单仅作回显。新增/修改/复制、默认值、省略/null、原空文本清空、复制目标失效、长度拒绝与后段故障回滚已接通。同表父列选择自身/后代拒绝成环，合法迁父/恢复根可用；同表其他关系合并范围仍明确拒绝。原输入外观、组件及交互保持，来源写入不额外要求list，清空不读取目标。详见[级联写入合同](cascade-tree-write-contract.md)。

最终相关95/95、零跳过，含实际生成编译、独立包/能力门槛、关系/条件及两个真实MySQL用例；写入实库用例分别执行整数/字符串模式，验证实际RR旧快照、当前名称/祖先状态、校验后独立UPDATE祖先1205及提交后释放。产品源码未改后补原空文本清空断言，实库写入再次1/1，与组合不累计；固定身份HTTP不当作正式登录或浏览器。首轮91通过3失败为生成源中文转义断言错误，原件保留，后续10/10及95/95各自记录。

写入系列10批20表及读取系列16表独立只读核零，未补DROP或增加观测授权。平台199、示例16项两轮正式生成与原来逐字一致，24手写partial保持，原uni-app前端/共享模板目录、平台元数据和现有生成产物无diff。既有C#项目需配套再生成Service/DTO/Controller/写入schema并构建；已有列无DDL，缺名称列先走元数据及结构预览/同步。

完整路径展示、后代筛选、级联导入导出、跨表/父范围/非冗余、受管列扩展和冗余传播、原浏览器/跨端/性能等仍未完成，Full门槛保留；未合并main或部署生产，新提交CI另核。


## 自关联级联路径、筛选及读快照（2026-10-22）

原DisplayText与节点对象筛选协议已接C#固定图；精确/后代模式来自元数据，后代按父键分批和主键游标读取，5500叶节点不截断。列表/数量/统计/导出行范围/选择共用异步Query，1000行路径展示的目标读取不超过9次。来源动作授权后的固定图不加目标list门槛；候选/独立路径保留两页list。坏关联显示空路径且保留ID，筛选坏根拒绝，不返回部分祖先。

分批图与来源分页/统计共用适配器明确RR快照，弱外层事务拒绝；写响应路径采用当前读。实测图遍历后来源COUNT前迁父，本次分页/路径/统计保持旧快照、下一请求看到新树；RR旧快照后改名的写响应名称/路径均为新值。最终Data155/155和生成/级联101/101均零跳过；首轮Data154通过1跳过为缺平台元数据环境，保留原件后补环境验证全部通过。早期5/5、1/1、6/6与最终有重叠不累计；实际下载并打开XLSX验证筛选行数，未宣称级联单元格完整路径导出或浏览器。

本系列12表、读取系列20表和写入系列24表独立只读核零，未补DROP或授观测权。215正式产物两轮逐字不变，24手写partial及原前端/共享模板目录/平台元数据保持；仅补关键中文注释后再次生成稳定。Core/Data需同版更新并再生成服务/控制器/VO及相关合同，已有库无DDL/回填。详见[级联查询合同](cascade-tree-query-contract.md)。

Full门槛继续保留：原浏览器/跨端/性能、级联导入导出格式、跨表/父范围/非冗余、受管列/隐式名称和冗余传播等仍待完成；当前64层/候选500项边界未解除，不合并main或部署生产，新提交CI另核。

## 自关联级联完整路径 Excel（2026-10-22）

原页面、按钮、模板及交互保持；固定冗余 selfTree 生成后端支持完整路径导出、逐层唯一名称反查，数字单元格按名称处理，不能冒充 ID。内部 Import 按来源 import 动作进入私有生成 Core，不增加普通 create/update 或目标 list 门槛；普通写入与候选规则保持，目标项目/软删/固定条件始终有效。详见[Excel合同](cascade-tree-excel-contract.md)。

相关组合 112/112、零跳过，前序离线 8/8 和实库 1/1 重叠不累加。四种来源/父键类型真实生成编译；Server-200 两种模式实测 XLSX 下载读取及原模板可写列回传、默认值/省略/null、追加/覆盖、源拒权、第二行及手写规则/后段故障整批回滚、目标行锁 1205 与提交后释放。专项身份和 HTTP 不等同正式登录、原浏览器或设备。

本系列 8 表，另读取 24、写入 28、展示 16 表，独立新连接均核零，不补 DROP 或授观测权；旧失败保留。215 正式产物两轮逐字不变、24 手写 partial 不变，原前端/共享模板/平台元数据与现有产物无 diff。无新增 DDL/回填，Core 同版更新后再生成。其它模式、隐式名称、传播、原浏览器/跨端/同条件性能和完整首版仍待完成，Full 门槛保持；不合并 main 或生产部署，新提交 CI 另核。

## 原级联隐式叶子名称（2026-10-22）

已修复页面只配置级联 ID 时缺少叶子名称的 C# 响应：固定 selfTree 同前缀物理受管名称按原协议隐式投影，原完整实体回传只允许只读 echo，严格 DTO 不开放；不扩大其它隐藏字段或其它页面，也不改变前端。用途 IR 分别登记该名称与计算路径，但用途 SQL 投影整体尚未接线，不报成已完成。详见[隐式名称合同](cascade-tree-implicit-name-contract.md)。

最终相关组合 153/153、零跳过；随后仅加强完整生成冲突反例并确认失败批次无写入，5/5 通过，前序 41/41 重叠不累加。真实 MySQL 两种键类型各执行有/无单独名称页面字段，原响应、完整表单回显、名称重算、清空、复制、拒权、防环、事务回滚和行锁通过。独立核零：写入 44 表、读取 28 表、展示 20 表、Excel 12 表；215 正式产物两轮逐字不变，24 手写 partial 保持，原前端/模板/平台元数据无 diff。

这关闭固定子集的详情隐式叶子名称待办；物理辅助列自动扩展、其它模式/传播、用途 SQL 整体投影、原浏览器/设备、同条件性能和 MySQL 完整首版仍待完成。无 DDL/回填，已有 C# 应用更新生成器后再生成相关 VO、Controller、Service；Full 门槛保持，新提交 CI 另核，不合并 main 或生产部署。

## selfTree自动名称与显式结构同步（2026-10-22）

共用Core规则从全局selfTree派生可空物理名称，结构预览、基础存储和C#生成一致；不写配置表或增加页面字段，不启动/生成DDL。显式名称优先，稳定派生ID保留命名空间；字符串继承长度、空长度及int64扩展名称使用50。原响应、完整表单只读echo、严格DTO、最终Service名称维护和原Excel流程沿既有合同。详见[自动名称合同](cascade-tree-storage-contract.md)。

最终Data161/161、相关组合157/157，均零跳过；初次夹具缺using未进入实库，修正后两个旧断言不再适用，原155通过2失败保留，更新为自动写入计划可用且Full门槛仍在后全部通过，不累计批次。四种键组合真实生成编译，MySQL六组键类型×显式/隐藏/自动名称验证；旧表预览只产生一项可空加列而不改表，执行后原行保留、名称NULL，随后生成HTTP默认值/清空/回显/复制/防环/拒权/回滚/锁通过。Excel四组模式验证真实XLSX及原模板回传、整批事务与权限。

独立核零：写入68、读取36、展示28、Excel28表；不补DROP或观测授权。平台199/示例16项两轮正式生成逐字一致，24手写partial与原前端/模板/元数据保持。已有应用需Core/Data同版更新，预览确认并显式同步物理结构，核验后再生成和构建；不自动回填旧行。其它级联/传播、用途SQL整体投影、原浏览器/跨端/同条件性能及完整MySQL首版仍未完成，Full门槛不解除，新CI另核，不合并main或部署生产。

## 同表非父级联与DECIMAL默认值（2026-10-22）

同表非父关联现按正常Mapper写入后的实际数据库状态，在通知前复核路径、归属和父拓扑，仍处于原事务；不临时写入或授权后补写。字符串排序、DECIMAL舍入、NULL与固定条件沿真实SQL，不用内存猜比较。图依赖变化即复核，省略名称保留；清空不要求目标list，显式重选沿原目标list，内部Import及私有结构读取不开放为请求标志。空/异树书签也不能造父环，64层允许、第65层拒绝。见[同表合同](cascade-tree-same-row-contract.md)。

Data170/170、相关组合162/162，零跳过，前序14和5重叠不累加。四种类型实际编译，两种正常MySQL及短列模式通过；覆盖当前新值/隐藏范围/循环、E/e与é的真实排序、0.014→0.01拒绝及0.015→0.02允许、权限/清空、复制/导入及整批回滚、通知后故障回滚与锁1205。非严格自有会话把FK截成另一有效ID也被最终核验拒绝，新连接模式不变，无全局设置或观测授权变化。

实库首轮发现DECIMAL默认值1/1.00造成伪DDL和核验失败，已在MySQL比较及列指纹做无精度丢失的文本归一；真实数值/舍入/字符串/缺省差异不吞掉。初次清理异常遮蔽首因、短列种子失败和缺pin的Data159+2fail均保留原证据，后续补首因保存和正确环境再验；原空样本按完整快照与零行核实后清理，不改写失败。

独立核零：同表10表、写入80、读取40、展示32、Excel36表。215项正式产物两轮逐字不变，24手写partial与原前端/模板/元数据保持。此功能无新增业务DDL或回填；更新Data及生成器，再生成相关后端并构建。Full门槛、跨表/来源父范围/非冗余/传播、用途SQL整体投影、原浏览器/跨端/同条件性能和完整首版仍未完成；新CI另核，不合并main或生产部署。


## 同表树完整分支深度校验（2026-10-22）

直接父字段现提前拒绝64层父路径，写后再验证当前节点的完整子树；同表非父字段共用该拓扑路径。200父键分批、1000主键游标页覆盖全部活跃后代，包含页面常量隐藏的节点，不受候选500项截断；真实Actor、项目和软删除范围保持。选中目标仍应用原权限与完整条件。换父不能把后代推到第65层，当前读锁保持至事务结束，失败整笔回滚，原页面/样式/交互和接口不变。见[完整分支合同](cascade-tree-topology-contract.md)。

专项10/10与最终相关组合163/163均零跳过，两批重叠不累加。整型/字符串父键×显式/隐藏/自动名称六种配置覆盖新增、复制64/65层、完整分支迁移、1305子节点末页隐藏后代、同表非父场景、独立连接1205与提交后释放、通知前故障回滚。初次9通过1失败为测试请求误带隐藏status；改测试元数据默认值后重跑，不扩大页面字段，原TRX保留。Data170/170是上一已验证批次，本批没有改Data或重复宣称新测。

最终独立新连接核零：读取44表、写入106表、展示36表、Excel44表、同表19表，均保留原失败及清理收据。用户授权六张旧失败表也已另行只读复核不存在，未重复DROP。215项正式产物两轮逐字相同，24个手写partial不变；前端、模板、平台元数据和现有产物无diff。源码及测试哈希与最终回归一致。没有新增观测权限、DDL或回填；已有C#应用更新生成器并再生成后构建即可。

完整分支读取与锁成本随树规模增加，尚未完成Java/C#同条件性能验收；其它级联、全局名称传播、用途SQL整体投影、完整原浏览器与设备、MySQL首版及后续数据库继续实施。FullApplication和候选/路径限制保留，不合并main或部署生产。


## 原级联的来源父范围（2026-10-22）

级联读取编译器不再重复拒绝已有标准来源父Scope的普通冗余页面。原候选/路径不新增来源父参数，仍按来源/固定目标list及目标项目/软删/常量；来源CRUD、查询和导入沿既有父范围隔离。HTTP冲突父键继续拒绝，已选越界仍整批拒绝；同父导入覆盖不改变异父同名记录。见[来源父范围合同](cascade-tree-source-parent-contract.md)。额外目标父参数、同表叠加父范围等组合另行验收，不宣布整个控件完成。

最终相关166/166零跳过，前序专项3/3重叠不累加。两种同型键实际生成编译和MySQL验证分组查询/统计/详情/已选/候选、真实XLSX、新增更新复制DELETE、父键冲突、外项目/墓碑/不存在父拒绝、同父覆盖与整批回滚、父行锁1205及释放。初次夹具编译错误未进库；前序2通过1失败及首轮相关165通过1失败分别是已选与父键冲突的错误测试预期，原记录保留，未修改拒绝规则。宽树样本改为250条参数化批次，1305节点及全部边界断言不变，不将测试准备加速当作业务性能验收。

独立连接累计核零：来源父范围18表、读取52、写入130、展示44、Excel60、同表25表；失败及清理收据保留。正式215产物两轮逐字不变，24手写partial不变；原前端、共享模板、平台元数据与现有产物无diff。Data170/170仍为前序已验证结果，本批未修改Data。已有C#应用更新生成器并再生成后构建，无新增DDL/回填/前端变化。FullApplication门槛及完整MySQL、浏览器/设备、同条件性能和其它数据库待办保持，不合并main或生产部署。
