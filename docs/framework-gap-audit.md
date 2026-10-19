# 主框架功能收口审计

审计日期：2026-10-19。原 YZP 只读参考为 `D:\\svn\\yzp`，审计时 HEAD 为 `06066f79`；Metarill 初次审计基于 `7de6c8b`；当前持续更新包含已集成的配置候选、生成查询交互和原媒体控件切片。源码事实、已有验收和待完成事项分开记录；后续每个切片应更新本文件，不能把静态源码存在当作完整验收。

## 范围与已经替换的设计

保留项目、后台、表字段、页面字段、按钮、主子关系、生成、自举、共享运行时和 uni-app。客户银行、合同、回款等专用业务不计为必须迁入的公共能力。C# `partial` 已替代 Java 的生成基类扩展方式；旧 Java v7/v8 选择、ServiceU、旧双菜单表和客户端携带数据库连接执行 SQL 不属于应盲目恢复的目标。

平台与独立包现已共用文件和 XLSX 模块；平台文件按已发布项目选同一业务会话，公开项目提示只定位已发布槽，文件行仍需实际项目、ready/PUBLIC 校验。新增准备守卫在生成写入首次对象锁前取得平台配置项目锁。上述已完成能力不应在后续任务中重新列为未实现。

## 一、生成页面的列表、详情和主子关系

**已有：** [BackendRenderer](../backend/src/Metarill.Generation/BackendRenderer.cs) 的 Query、ParentValidation、Mutations 已实现开放筛选、排序、selectedIds、分页/count、parentId 受限等值范围和真实父记录校验。

**本轮补齐：** [list.vue.tpl](../backend/templates/uniapp/list.vue.tpl) 按 Searchable/Sortable 白名单编译筛选、排序和分页快照，导出复用已确认筛选；零、false、长标识与日期范围保留。列表/详情消费真实常量 `xxxShow` 和已有关系别名/合法选项映射，缺失关系名保持原标识。RTF 沿原事件且未编辑不写回，FileLibrary 编译已声明的访问及数量/类型配置；未知或坏配置拒绝生成。

**仍缺：** 多选页“仅看已选”、完整关系名称读计划、详情分组、条件显示/只读、完整表单规则和配置按钮。`ConfigChildren` 目前只在 Core、种子和仓储中保留，生成器未消费；当前脱敏平台模型有 8 条主子关系。

**原实现证据：** YZP `service/yzp/v8/UniappService.java` 的 `1421` 附近按 ConfigChild 排序和 childPageOpenType 生成子页面，并传递父记录与只读上下文；`2001` 附近生成排序和筛选组件。`JavaService.java` 的 `1748` 附近及 `V8ControllerRenderer` 的 page 返回还支持独立 statistics。C# 当前有 count，不能据此声称已具备原聚合统计协议。

**可验收切片：** 从模板编译已声明的列表筛选/排序，再编译父页到子页的导航和弹层。真实生成样板验证跨页选择、刷新、分页、父级保存前禁写子级、伪造 parentId 拒绝、父只读状态、导出复用筛选。平台原复制前端能显示子表，不等于再生成产物具备相同能力。

## 二、关系控件、联接与存储模式

**已有：** [RelationCompilation](../backend/src/Metarill.Generation/RelationCompilation.cs) 可编译明确 List 目标和范围；ForeignValidationRenderer 校验关系记录存在、未删除和同项目。SelectPage 已有简单单选导航。

**缺口：** RelationCompilation 仍只编译 List。本次后端已增加固定 `/page` 关键字分页与 `/selected` 有界回读，旧数组超过 5000 项整体错误；两种父范围分别验证，详见 [关系候选合同](relation-options.md)。新生成 List 已通过原 zxz 选择器的可选远程模式接入分页/已选回读；当前页标签按固定 selected 分批读取，未知双父保留原ID只读提示。未再生成的旧页面仍可能调用旧数组；浏览器/跨端验收和独立包运行时装配仍须分别验证。MultiSelectPage、MultiCheckBoxDatabase、CascadeTree、LevelList 等在 [ControlRenderer](../backend/src/Metarill.Generation/ControlRenderer.cs) 仍只读保护。没有原非冗余读计划、冗余/非冗余混用传播与维护回填。当前 `supportsNonRedundant=false` 是能力声明，不是功能完成。平台仍保留三个 isJoinColumn 字段定义，保留定义不等于通用派生和反向传播已经实现。

**原实现证据：** YZP 的 `NonRedundantReadPlanCompiler`、`MixedRelationPropagationCompiler`、运行时 list/selectpage/cascade，以及 `SystemMaintenanceBusiness` 的回填入口。只读引用扫描可识别这些配置声明，不能因扫描命中就宣称对应控件执行能力已移植。

**可验收切片：** 先完成候选关键字分页与单选/多选闭环，覆盖超过 5000 条、已选但不在当前页、删除/越权目标和稀疏更新；之后再单独实现级联和派生读/传播，验证更新与删除事务、关系名称导入以及多值分页不倍增。原生 SQL 保留方言边界，不自动猜译。

## 三、配置候选、字段引用和工作台断链

**本次补齐：** [ConfigurationCandidatesController](../backend/src/Metarill.Api/Controllers/ConfigurationCandidatesController.cs) 保留 `list_control_tables`、`select_page_options`、`references`、`clear_page_control_detail`。表/页候选只返回 id/name/value/text，页面仅 type=1，范围来自真实网页登录管理员当前项目。引用响应保留页面、索引和精确控件映射定位，不返回控件 JSON、SQL 或脚本原文。清空页面覆盖通过统一配置准备守卫先锁项目，在原事务内全量验证实际字段/表/页归属后只改 control_detail 与审计，不改基础字段定义和其它页面属性。新增 `control_metadata` 只投影编辑器所需的 controlKind/columnName/columnType/name/controlDetail：要求真实网页登录管理员，从当前项目有效字段推导并验证有效父表。明细保持已存完整字节，不执行其中的 SQL/脚本，也不返回页级 SQL 或连接。编辑器已改调此专用接口，标准 detail 的 parentId 范围保持严格。

**原实现证据：** YZP `ConfigColumnController` 的上述四个方法、`ConfigColumnReferenceBusiness`、`ConfigPageColumnServiceImpl.clearControlDetailForColumn`；前端 `yzp-control-detail-editor.vue` 的 430/432 附近及批量字段页的 references 调用。

**仍待：** `join_column_candidates` / `save_join_columns` 尚未移植，保留的 add_join_columns 页面调用会失败；本轮没有实现联接保存、非冗余传播或关系分页 UI。字段继承详情断链已在本次修复，未扩大标准生成页面范围。

**验收边界：** 新接口需真实 API 验证旧表单/JSON 编码、跨项目、重复键及未知字段拒绝，清空多页失败无部分提交、基础明细和其它字段不变。专用字段详情另测完整条件字节保持、普通账号/匿名/跨项目/不存在/软删字段及父表拒绝；前端运行实际方法验证继承条件、缓存和迟到回调。页面选择器的组件许可、完整浏览器交互依对应台账另行确认。

## 四、独立生成应用的前端、认证与运行时

**已有：** [ApplicationPackager](../backend/src/Metarill.Generation/ApplicationPackager.cs) 携带 Core/Data/Exports/Files、后端宿主和生成 Vue；后端已完成独立包真实 MySQL 闭环，开发认证只在显式 Development 配置下启用，普通启动不 DDL。

**缺口：** 独立包没有完整 uni-app 工程、登录/主框架、common、组件、菜单/权限等配套运行时，只有 Vue 片段及 pages.generated.json；包内正式认证授权及 Agent 宿主也未独立完成。平台前端能运行和包内后端能编译均不代替独立应用前端验收。

**原实现证据：** YZP `template/v8/runtime/uniapp` 与运行时同步包括 main.js、common、components 和通用页面；当前 C# packager 的基础工程集合不包含这些宿主资源。

**可验收切片：** 建立经过许可核对的确定性前端运行时清单，装配独立 HBuilderX Vue3 工程，固定页面/权限契约且不带平台配置页或环境凭据；在离开原仓库的目录完成 HBuilderX 构建和浏览器登录→列表筛选排序→父子 CRUD→文件/导入导出。小程序、Android、iOS 真机仍分别验收。

## 五、本地生成助手和既有项目跟进

**原实现证据：** YZP `tools/socketClient/client.js` 接收 writeFileV2；`generation-files.js` 负责跨进程目录锁、项目归属、手写保护和回执。旧助手另有 SQL 网关，这部分不应照搬到已按服务端配置数据库的 C# 架构。

**已有与缺口：** 当前 CLI 可 generate/package，CSharpGenerator 有归属摘要和手写保护，独立包可以下载；尚无本地接收已审核生成计划、更新已有应用及同步运行时的完整通道。下载 ZIP 不等于可以覆盖现有应用。平台 main.vue 仍有 generate_project_config / sync_runtime / generate_all_page 的旧直调，C# 未提供对应旧端点，需要按新目标实现或明确接到当前流程。

**可验收切片：** 生成助手先做本地只读预检，再校验项目/版本/manifest/hash 与受管路径，原子应用生成产物、首次创建手写壳、保留用户配置，最后返回明确回执。验证重复生成、手写修改、重叠目录/链接、并发、失败恢复和离线中断；不能提供远程任意路径写入或客户端 SQL 执行。

## 六、数据库反查与显式变更链路

**已有：** [SchemaService.InspectAsync](../backend/src/Metarill.Data/Schema/SchemaService.cs) 已校验真实目标并读取受管表、列、索引和 SHOW CREATE 快照；Preview/Apply 有计划和执行核验。不能把这部分写成“完全没有反查”。

**本次补齐：** HTTP `POST /api/admin/sys/database/inspect` 与 MCP `inspect_database_schema` 复用只读受管结构服务，校验真实管理员当前项目与有效表归属，组合文件基础模型，响应固定 `executable:false`、结构快照/指纹及缺失表名。真实 HTTP/MCP 指纹一致，读取前后执行历史不变。

**仍缺：** CLI 专用反查入口、未登记表的安全目录和待确认元数据导入计划。当前普通结构预览不是反向创建元数据。原 YZP SchemaClient 主要是配置到 DDL 的同步，未发现已完成的全库逆向导入成品；完整逆向导入应记为本计划待交付，不冒称旧功能被移除。MCP preview_database_schema 已在集成切片与 HTTP 完整预览统一采用文件基础设施组合，并通过真实 MCP 客户端/HTTP 指纹相等回归。

**可验收切片：** 先只读开放已授权项目连接的表结构反查，区分受管/未受管范围，SQL 只作预览、`executable: false`；未知属性和缺少定义拒绝。之后单独实现结构→逻辑模型→差异确认→元数据事务保存，禁止读取后自动写元数据或执行 DDL。覆盖 MySQL 类型/默认值/索引/外键、目标库不一致、视图/未知类型和结构并发漂移。

## 建议推进顺序与发布门槛

1. 配置候选/引用接口 → 关系候选分页与单多选闭环 → 生成列表/详情/父子页面交互。
2. 确定性独立前端运行时 → 独立应用认证与权限 → 本地生成助手安全跟进现有应用。
3. 只读结构反查可平行推进；逆向导入、联接派生/传播、维护回填分别设事务和并发验收。

首期发布还需核对原通用文件的 S3/云存储边界、完整 Agent/浏览器授权、跨端真机以及同数据/SQL/索引/资源的 Java 性能对照。后续 PostgreSQL、SQL Server、MariaDB、Oracle、SQLite 依计划分批，不因驱动能连接宣布产品支持。失败、跳过、静态构建和真实验收必须保留区别，不能以一个切片完成代替完整 YZP 替代。

## 本次配置切片验证

2026-10-19 在独立普通 clone 验证：`Metarill.Metadata.Tests` 64/64、`Metarill.Api.Tests` 16/16、前端 `node --test tests/*.test.mjs` 198/198，全部零跳过。后端使用显式隔离的 Server-200 MySQL 测试库；候选新增用例只创建唯一测试元数据，通过统一删除入口清理，不创建业务物理表。前端新增三项直接执行真实组件方法，覆盖专用接口载荷、继承配置/条件、缓存和迟到/失败响应。

这些结果确认本切片规则及 HTTP/运行时接线，不替代 HBuilderX 完整构建、浏览器或移动端验收，也不代表其余五类缺口已完成。本切片未修改元数据主文件或生成 `.g.cs`，未实现 join 列保存、非冗余传播和关系分页 UI。

## 生成查询与真实界面补充证据

2026-10-19 协调者按当前模板与含四项索引的最终元数据再生成平台/示例产物；再次生成的 215 个文件字节一致，24 个手写 partial 摘要不变。真实生成 Vue 方法探针覆盖筛选/排序/乱序/父上下文、媒体事件与空值语义，不能代替浏览器。完整管理端前端回归 217/217 通过，HBuilderX Vue3 实际 Web 发行成功。

真实浏览器从空白表单选择原后台选择器，新建唯一临时表，批量预检并保存两个字段，原按钮进入结构预览；只预览一条 CREATE TABLE，显式执行后重新读取数据库剩余差异为 0。临时配置已通过标准界面软删除，列表只剩原 demo_customer；物理清理前验证本轮表原先不存在、当前为空且配置已软删，未操作既有表。此证据补齐了纯界面建表；完整生成应用业务/主子/多选和三类真机仍按上述缺口继续验收。

受管反查补充验证：协调者真实 HTTP 1项、真实 MCP 既有用例增强1项及查库前授权拒绝3项均通过、0跳过。详见 [结构反查合同](schema-inspection.md)。该增量尚未推送时不混记上轮 d42e360 的 CI。上轮 [Actions](https://github.com/zhao-yc/metarill/actions/runs/39199182693) 的 Linux、Windows、前端三项均成功，其中纯图片解码回归在 Linux 实际执行；数据库场景仍依独立库 TRX。
