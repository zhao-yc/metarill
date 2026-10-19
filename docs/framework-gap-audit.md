# 主框架功能收口审计

审计日期：2026-10-19。原 YZP 只读参考为 `D:\\svn\\yzp`，审计时 HEAD 为 `06066f79`；Metarill 初次审计基于 `7de6c8b`；当前状态复核包含 `cc63976` 及统一再生成产物：配置候选、主子交互、完整独立前端、List 关系分页 UI、type=2 独立多选页、本地受控源码更新和独立宿主只读结构命令。源码事实、已有验收和待完成事项分开记录；后续每个切片应更新本文件，不能把静态源码存在当作完整验收。

## 范围与已经替换的设计

保留项目、后台、表字段、页面字段、按钮、主子关系、生成、自举、共享运行时和 uni-app。客户银行、合同、回款等专用业务不计为必须迁入的公共能力。C# `partial` 已替代 Java 的生成基类扩展方式；旧 Java v7/v8 选择、ServiceU、旧双菜单表和客户端携带数据库连接执行 SQL 不属于应盲目恢复的目标。

平台与独立包现已共用文件和 XLSX 模块；平台文件按已发布项目选同一业务会话，公开项目提示只定位已发布槽，文件行仍需实际项目、ready/PUBLIC 校验。新增准备守卫在生成写入首次对象锁前取得平台配置项目锁。上述已完成能力不应在后续任务中重新列为未实现。

## 一、生成页面的列表、详情和主子关系

**已有：** [BackendRenderer](../backend/src/Metarill.Generation/BackendRenderer.cs) 的 Query、ParentValidation、Mutations 已实现开放筛选、排序、selectedIds、分页/count、parentId 受限等值范围和真实父记录校验。

**本轮补齐：** [list.vue.tpl](../backend/templates/uniapp/list.vue.tpl) 按 Searchable/Sortable 白名单编译筛选、排序和分页快照，导出复用已确认筛选；零、false、长标识与日期范围保留。列表/详情消费真实常量 `xxxShow` 和已有关系别名/合法选项映射，缺失关系名保持原标识。RTF 沿原事件且未编辑不写回，FileLibrary 编译已声明的访问及数量/类型配置；未知或坏配置拒绝生成。

**主子编译与权限：** [ChildrenCompilation](../backend/src/Metarill.Generation/ChildrenCompilation.cs) 已消费 `ConfigChildren`，校验父/子页、同后台归属、唯一父范围和对应外键，拒绝循环、未知配置及不支持的列表类型，按排序和打开方式生成子模块。父详情支持 Tab，打开方式 1 为新页面、2 为 PC 弹层；未取得权限、父记录未保存或回读未就绪时不开放子写入。生成页结合页权限、父只读上下文和已声明可读字段的只读条件限制操作；服务端仍独立验证父记录、项目和 CRUD 权限，前端禁用不替代授权。

**仍缺：** 完整关系名称读计划、详情分组、复杂条件显示/只读、完整表单规则和配置按钮。type=2 独立多选页的跨页草稿、仅看已选和完整确认回读已补齐；这不等于 MultiSelectPage 字段协议已接线。独立包主子 Tab/PC 弹层和多选页 Web/浏览器已有实际证据，真机仍待验收。

**原实现证据：** YZP `service/yzp/v8/UniappService.java` 的 `1421` 附近按 ConfigChild 排序和 childPageOpenType 生成子页面，并传递父记录与只读上下文；`2001` 附近生成排序和筛选组件。`JavaService.java` 的 `1748` 附近及 `V8ControllerRenderer` 的 page 返回还支持独立 statistics。C# 当前有 count，不能据此声称已具备原聚合统计协议。

**界面验收范围：** 独立生成样板已实际验证筛选/排序、父子 Tab/PC 弹层 CRUD、子刷新保留父脏字段、父保存前不挂载子表、父只读和最终回读；type=2 页另验两页21项草稿/取消/全选/仅看已选/完整确认。新页面打开方式、复杂父范围和导出复用筛选的浏览器验收仍待完成；伪造 parentId 由真实 HTTP 权限用例验证，不冒充浏览器操作。平台继承页与离线再生成页分别验收。

## 二、关系控件、联接与存储模式

**已有：** [RelationCompilation](../backend/src/Metarill.Generation/RelationCompilation.cs) 可编译明确 List 目标和范围；ForeignValidationRenderer 校验关系记录存在、未删除和同项目。SelectPage 已有简单单选导航。

**缺口：** RelationCompilation 仍只编译 List。本次后端已增加固定 `/page` 关键字分页与 `/selected` 有界回读，旧数组超过 5000 项整体错误；两种父范围分别验证，详见 [关系候选合同](relation-options.md)。新生成 List 已通过原 zxz 选择器的可选远程模式接入分页/已选回读；当前页标签按固定 selected 分批读取，未知双父保留原ID只读提示。同ID跨不同目标父组时保守保留原ID，不借用另一组成功回读的标签；该双父缓存/标签串用 P2 已修复，关系 UI 子任务 Node 回归 229/229。未再生成的旧页面仍可能调用旧数组；独立包关系 wrapper、两个相关 runtime 及 selectionPageMixin 均已进入明确资产清单，随包闭包检查和 HBuilderX 通过。远程 List 的大数据搜索/分页浏览器与跨端验收仍待分别补验，type=2 多选页浏览器证据不能替代它。MultiSelectPage、MultiCheckBoxDatabase、CascadeTree、LevelList 等在 [ControlRenderer](../backend/src/Metarill.Generation/ControlRenderer.cs) 仍只读保护。没有原非冗余读计划、冗余/非冗余混用传播与维护回填。当前 `supportsNonRedundant=false` 是能力声明，不是功能完成。平台仍保留三个 isJoinColumn 字段定义，保留定义不等于通用派生和反向传播已经实现。

**原实现证据：** YZP 的 `NonRedundantReadPlanCompiler`、`MixedRelationPropagationCompiler`、运行时 list/selectpage/cascade，以及 `SystemMaintenanceBusiness` 的回填入口。只读引用扫描可识别这些配置声明，不能因扫描命中就宣称对应控件执行能力已移植。

**待实际界面验收：** 核验已实现的 List 候选关键字分页、单选/多选与名称回读，覆盖超过 5000 条、已选但不在当前页、删除/越权目标和稀疏更新；之后再单独实现级联和派生读/传播，验证更新与删除事务、关系名称导入以及多值分页不倍增。原生 SQL 保留方言边界，不自动猜译。

## 三、配置候选、字段引用和工作台断链

**本次补齐：** [ConfigurationCandidatesController](../backend/src/Metarill.Api/Controllers/ConfigurationCandidatesController.cs) 保留 `list_control_tables`、`select_page_options`、`references`、`clear_page_control_detail`。表/页候选只返回 id/name/value/text，页面仅 type=1，范围来自真实网页登录管理员当前项目。引用响应保留页面、索引和精确控件映射定位，不返回控件 JSON、SQL 或脚本原文。清空页面覆盖通过统一配置准备守卫先锁项目，在原事务内全量验证实际字段/表/页归属后只改 control_detail 与审计，不改基础字段定义和其它页面属性。新增 `control_metadata` 只投影编辑器所需的 controlKind/columnName/columnType/name/controlDetail：要求真实网页登录管理员，从当前项目有效字段推导并验证有效父表。明细保持已存完整字节，不执行其中的 SQL/脚本，也不返回页级 SQL 或连接。编辑器已改调此专用接口，标准 detail 的 parentId 范围保持严格。

**原实现证据：** YZP `ConfigColumnController` 的上述四个方法、`ConfigColumnReferenceBusiness`、`ConfigPageColumnServiceImpl.clearControlDetailForColumn`；前端 `yzp-control-detail-editor.vue` 的 430/432 附近及批量字段页的 references 调用。

**仍待：** `join_column_candidates` / `save_join_columns` 尚未移植，保留的 add_join_columns 页面调用会失败；联接保存与非冗余传播仍未实现；关系分页 UI 已由后续切片接入，范围与待验收边界见第二节。字段继承详情断链已在配置切片修复，未扩大标准生成页面范围。

**验收边界：** 新接口需真实 API 验证旧表单/JSON 编码、跨项目、重复键及未知字段拒绝，清空多页失败无部分提交、基础明细和其它字段不变。专用字段详情另测完整条件字节保持、普通账号/匿名/跨项目/不存在/软删字段及父表拒绝；前端运行实际方法验证继承条件、缓存和迟到回调。页面选择器的组件许可、完整浏览器交互依对应台账另行确认。

## 四、独立生成应用的前端、认证与运行时

**已有：** [ApplicationPackager](../backend/src/Metarill.Generation/ApplicationPackager.cs) 携带 Core/Data/Exports/Files 和后端宿主，默认 FullApplication 由 [FrontendPackager](../backend/src/Metarill.Generation/FrontendPackager.cs) 组装独立 HBuilderX uni-app Vue3 工程，包含登录、菜单、common、受控组件、页面写入 schema 和生成 list/detail。固定逐文件清单与逐层链接拒绝保护来源边界，默认 AppID 为空，不复制平台配置页或环境凭据；非空输出目录拒绝覆盖手写宿主。未知或未接线 CustomControl（含只读详情）在写入前明确拒绝。CLI 可显式选择 BackendOnly，平台下载按服务端已发布身份选择该模式，manifest/README/响应提示不含完整前端，不能默默降级业务包。

**认证与证据边界：** 后端已有独立包真实 MySQL 闭环；开发认证只在显式 Development 配置下启用，固定包内单项目，不接收 query 身份，普通启动不 DDL。同一条件下仅允许 HTTP(S) 回环 Origin 的开发跨端口预检，允许来源不产生身份。真实 Node/HTTP 脚本已执行登录→菜单→生成 list/detail 新增、修改、删除与注销失效，初始 AppID 空值和开发者定制后运行校验分开验证。正式认证授权与 Agent 宿主仍待接入；完整独立包 Web 发行、核心主子和独立多选页浏览器已经协调者实测，文件/导入导出/远程关系大数据浏览器及真机仍待验收。

**来源与下一步：** 原 YZP `template/v8/runtime/uniapp` 的通用运行时由受控资产清单复用，详情见 [独立应用包](standalone-application.md)。协调者的私有450文件主子 v2 包已完成装配、依赖闭包、后端编译、显式安装后0差异、实际 HBuilderX 发行与浏览器登录/菜单/父子 CRUD；另一448文件 type=2 包补验两页21项选择。临时服务、凭据和精确样本均已清理。继续分别补文件/导入导出、远程 List 浏览器和小程序/Android/iOS 真机，不能扩大这两批证据范围。

## 五、本地生成助手和既有项目跟进

**原实现证据：** YZP `tools/socketClient/client.js` 接收 writeFileV2；`generation-files.js` 负责跨进程目录锁、项目归属、手写保护和回执。旧助手另有 SQL 网关，这部分不应照搬到已按服务端配置数据库的 C# 架构。

**已有与缺口：** CLI 已有 generate/package，以及 FullApplication 的 update preview/apply/recover；项目配置、运行时、全部页面三个独立范围校验依赖闭包并保留手写和配置，失败恢复绑定原计划及旧归属，详见[本地源码更新](local-generation-update.md)。CLI 不执行 SQL、依赖安装或服务。平台下载和本地受控更新仍是两个入口；自动接收通道尚未接线，下载 ZIP 不等于可以覆盖现有应用。平台 main.vue 仍有 generate_project_config / sync_runtime / generate_all_page 的旧直调，C# 未提供对应旧端点，需要按新目标实现或明确接到当前流程。

**已有本地更新证据与下一切片：** 已覆盖三范围、依赖不足拒绝、归属/摘要与路径、首次创建手写壳、配置逐字保护、并发锁、失败/离线恢复及候选完整能力门禁。逐文件刷盘替换和回滚不宣称跨文件系统事务；用户编辑出现第三态保留现场。仍需把平台三个旧动作安全接到明确请求与本机预检，返回实际回执，不能提供远程任意路径写入、客户端 SQL 或把三个动作伪装成同一个完整 ZIP。

## 六、数据库反查与显式变更链路

**已有：** [SchemaService.InspectAsync](../backend/src/Metarill.Data/Schema/SchemaService.cs) 已校验真实目标并读取受管表、列、索引和 SHOW CREATE 快照；Preview/Apply 有计划和执行核验。不能把这部分写成“完全没有反查”。

**本次补齐：** HTTP `POST /api/admin/sys/database/inspect` 与 MCP `inspect_database_schema` 复用只读受管结构服务，校验真实管理员当前项目与有效表归属，组合文件基础模型，响应固定 `executable:false`、结构快照/指纹及缺失表名。真实 HTTP/MCP 指纹一致，读取前后执行历史不变。

**仍缺：** 平台 CLI 的选项目反查、未登记表的安全目录和待确认元数据导入计划。独立宿主 `--schema-inspect` 已固定包内受管模型，输出 executable:false，实际命令未改元数据或执行历史；它不等于平台 CLI 或未登记目录。当前普通结构预览不是反向创建元数据。原 YZP SchemaClient 主要是配置到 DDL 的同步，未发现已完成的全库逆向导入成品；完整逆向导入应记为本计划待交付，不冒称旧功能被移除。MCP preview_database_schema 已在集成切片与 HTTP 完整预览统一采用文件基础设施组合，并通过真实 MCP 客户端/HTTP 指纹相等回归。

**后续切片：** 在现有受管 HTTP/MCP 只读反查上补 CLI 和未受管表安全目录，保持 `executable: false`，未知属性和缺少定义拒绝。之后单独实现结构→逻辑模型→差异确认→元数据事务保存，禁止读取后自动写元数据或执行 DDL。覆盖 MySQL 类型/默认值/索引/外键、目标库不一致、视图/未知类型和结构并发漂移。

## 建议推进顺序与发布门槛

1. 主子 Tab/PC 弹层与 type=2 多选页独立包 Web/浏览器已补验；继续远程 List 大数据、文件/导入导出、新页面打开方式和其余业务范围。
2. 核验完整包运行时清单，继续正式认证适配、Agent 宿主和本地生成助手安全跟进现有应用。
3. HTTP/MCP 及独立宿主受管只读反查已开放；平台 CLI、未受管目录、逆向导入、联接派生/传播和维护回填继续分别交付与验收。

首期发布还需核对原通用文件的 S3/云存储边界、完整 Agent/浏览器授权、跨端真机以及同数据/SQL/索引/资源的 Java 性能对照。后续 PostgreSQL、SQL Server、MariaDB、Oracle、SQLite 依计划分批，不因驱动能连接宣布产品支持。失败、跳过、静态构建和真实验收必须保留区别，不能以一个切片完成代替完整 YZP 替代。

## 本次配置切片验证

2026-10-19 在独立普通 clone 验证：`Metarill.Metadata.Tests` 64/64、`Metarill.Api.Tests` 16/16、前端 `node --test tests/*.test.mjs` 198/198，全部零跳过。后端使用显式隔离的 Server-200 MySQL 测试库；候选新增用例只创建唯一测试元数据，通过统一删除入口清理，不创建业务物理表。前端新增三项直接执行真实组件方法，覆盖专用接口载荷、继承配置/条件、缓存和迟到/失败响应。

这些结果确认本切片规则及 HTTP/运行时接线，不替代 HBuilderX 完整构建、浏览器或移动端验收，也不代表其余五类缺口已完成。本切片未修改元数据主文件或生成 `.g.cs`，未实现 join 列保存、非冗余传播和关系分页 UI。

## 生成查询与真实界面补充证据

2026-10-19 协调者按当前模板与含四项索引的最终元数据再生成平台/示例产物；再次生成的 215 个文件字节一致，24 个手写 partial 摘要不变。真实生成 Vue 方法探针覆盖筛选/排序/乱序/父上下文、媒体事件与空值语义，不能代替浏览器。完整管理端前端回归 217/217 通过，HBuilderX Vue3 实际 Web 发行成功。

真实浏览器从空白表单选择原后台选择器，新建唯一临时表，批量预检并保存两个字段，原按钮进入结构预览；只预览一条 CREATE TABLE，显式执行后重新读取数据库剩余差异为 0。临时配置已通过标准界面软删除，列表只剩原 demo_customer；物理清理前验证本轮表原先不存在、当前为空且配置已软删，未操作既有表。此证据补齐了纯界面建表；完整生成应用业务/主子/多选和三类真机仍按上述缺口继续验收。

受管反查补充验证：协调者真实 HTTP 1项、真实 MCP 既有用例增强1项及查库前授权拒绝3项均通过、0跳过。详见 [结构反查合同](schema-inspection.md)。该增量尚未推送时不混记上轮 d42e360 的 CI。上轮 [Actions](https://github.com/zhao-yc/metarill/actions/runs/39199182693) 的 Linux、Windows、前端三项均成功，其中纯图片解码回归在 Linux 实际执行；数据库场景仍依独立库 TRX。

## 独立主子生成包实证跟进（2026-10-19）

前述“待主协调者Web/浏览器”现已部分补验：协调者将关系wrapper及两个runtime模块加入独立包明确allowlist，真实450文件FullApplication在新空目录完成依赖闭包、后端构建和HBuilderX Web发行；浏览器完成独立登录/菜单、父子Tab与PC弹层CRUD、子筛选/名称升降序、父脏字段保留和最终持久化、父只读继承及注销。生成与独立宿主81项、前端229项协调者组合回归均通过且无跳过；平台工程也实际发行Web成功。

这批浏览器只覆盖两条子行与1/1分页，没有把实际HTTP的5014候选验证算成远程关系控件浏览器验收；新页面打开方式、跨页选择、独立包文件/导入导出与真机仍须分别补验。`backend/generated`已按新模板统一再生，两次189项受管文件和2份归属清单字节一致，24个手写partial哈希不变；平台当前继承的前端页面和离线生成页面仍是不同验收对象，不自动互相替代。下一优先项为已有项目的受控本地更新与上述关系/跨端缺口，正式认证、Agent和性能门槛不变。

## 独立多选、本地更新和结构命令实证跟进（2026-10-19）

协调者集成三个新切片后，两次189项受管产物及2份清单字节一致，24个手写 partial 摘要不变。type=2 私有父页仅观察原 props/events，API 预置21行形成两页；浏览器实际验证跨页、全选本页、仅看已选、取消不写、确认完整字符串ID及确认前服务端最新名称。只读禁用写入口；选中后精确软删其中一项使确认整体失败，父值保持原21项，没有返回合法子集。首次真实 Vue 渲染发现 computed 未订阅 selectionReady 导致确认永远禁用，从源修复后重新导出 v2，HBuilderX 与浏览器复验成功；失败包和截图保留。

这批 private 父页不是生产 MultiSelectPage 字段接线，21行由 API 预置也不宣称纯界面新建。随机表先按完整计划显式创建，核验21行精确ID/最终字段及恰1条软删后只清理该表，清理后核验不存在；8099/5195已停，临时认证配置删除。独立结构命令另实际反查4张受管表，64位指纹与 executable:false，元数据摘要不变。本地更新完整20项及候选能力2项定向均由协调者实际通过；最初错误 filter 未命中用例的运行不计通过，最终全组合数字以验收台账新批次为准。
