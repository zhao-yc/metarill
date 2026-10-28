# 按用途读取投影编译合同

## 当前交付范围

ReadProjectionCompilation.ForPage(model, table, page) 是独立的生成期中间表示。输入必须为同一个 GenerationModel 中的真实表和页面；不接受客户端指定表、字段、SQL 或任意模型副本。

返回 PageReadProjections，分别包含 List、Detail、Selection。每个计划提供：

- PhysicalColumns：后续 SQL 必须明确选择的真实物理列。
- OutputFields：响应属性及来源。Physical 指向物理列；ConstantShow 指向原值列并携带原控件常量明细，不能当作数据库列。
- Dependencies：按稳定字段 ID 记录全部依赖理由。
- Gaps：未证明的读取或控件能力及字段位置。

集合按稳定字段 ID 去重、排序，主键优先。编译器不修改元数据、PageModel.Readable/Writable 或写入 DTO。

**本切片尚未接入 BackendRenderer、SelectionPageRenderer、能力审计或运行时。现有接口仍沿用原单 VO 与读取入口，不能表述成 SQL 已缩窄、接口已分离或性能已改善。CanCompile 只表示本固定读取计划未发现 Gap，不代表写入、候选范围、运行时权限或 FullApplication 所有能力均已完成；SourceSelectPage 指向 type2 的读取证明也不表示其保存已受支持。**

## 三种用途

| 用途 | 基础集合 | 明确追加的依赖 |
| --- | --- | --- |
| List | Id + 本页 ListColumns | 原列表实际显示的关系辅助名、常量 Show 的原值、逐行按钮的固定规则字段 |
| Detail | Id + 本页 DetailColumns | 原字段显示辅助名、唯一可读目标父外键、常量 Show、字段可见规则、详情按钮规则、页面及主子只读字段 |
| Selection | Id + 本页 ListColumns | List 依赖，以及实际入站选择控件的固定 nameField |

WHERE、排序、项目归属、软删除和父范围列不会仅因参与范围判断而加入响应。新增依赖始终受当前页 Readable 限制，不能借用同表另一页的可读权限。子页导航依赖父记录 ID，不因此自动输出隐藏父键。

关系辅助列复用 SelectionDisplayFieldCompilation 与原详情显示命名规则。原 UI 已明确回退 ID 时登记回退原因，不合成不存在的 Name 属性。已有辅助列未开放读取时保留 Gap，不偷偷开放。

## 原界面控件覆盖语义

两条原渲染路径有不同的配置取值规则：

- 原详情 OriginalUniappDetailFields.Control 取表级 Source.ControlKind；页级非空 controlDetail 仍可覆盖原明细。
- 原列表 Cell/Filters 取页级非空 controlKind。列表 _ids，以及控件非空的 _id，按原顺序显示辅助名；这个分支先于常量 Show。
- 列表的 SelectPage/MultiSelectPage 只改变名称展示或名称筛选输入，不建立详情选择弹层。因此入站确认只扫描真实 DetailColumns 中的表级 SelectPage/MultiSelectPage。页级 Text→Select 覆盖不会凭空生成入站选择；反向覆盖也不会消除原详情弹层。
- 原详情实际以目标页面 type=2 使用多选确认，复用 MultiSelectCompilation 证明读取所需存储、名称、父上下文与范围。表级 MultiSelectPage 指向非 type2 目标明确 Gap。

真实原 List/Detail 渲染矩阵验证正向、反向覆盖和网格独有字段。未知原详情控件、数据库多选、级联及不透明自定义控件仍 Gap，没有用普通文本占位或全列读取冒充复杂控件完成。

Selection 的入站 nameField 从已知根配置和页级明细覆盖中解析，按精确 pageId 定位目标。名称必须为该目标页可读的固定物理 string 或 Id；同表另一页可读不能替代。无法解析且无法证明目标身份的入站定义不猜目标，由来源 Detail 计划报告配置 Gap。

## 固定规则依赖

仅解释当前原 buttonVisibleRule.js 已有结构：

- 分组为 logic、conditions、groups，支持 AND/OR。
- 条件为 field、operator、value、valueList，支持 eq/ne/in/notIn/gt/gte/lt/lte/isBlank/isNotBlank。
- 只从 field 读取列；value/valueList 是常量，即使内容恰为字段名也不形成列依赖。
- 已证明的常量 xxxShow 条件追溯原值和 Show 输出。
- 列表仅 EditAndGrid 的逐行按钮读行对象；顶部 List 按钮、Hidden/OnlyDeve 等不读行规则。
- 原生 JS、未知规则键、重复键、点路径、不可读列、未知操作符或超出嵌套边界均 Gap，不静默当作无依赖。

规则回归执行仓库内真实原按钮模块，对比完整对象与窄对象求值，不依赖开发机 D 盘原仓库。

## 拒绝边界与后续接线

非冗余表、虚拟/联接列、不透明 CustomControl、未证明 JSON 点路径、未知父映射、输出别名冲突、无法证明的选择范围或排序均保留 Gap，不回退全表或整个 Readable。有 Gap 的计划可能仍列出部分已证明依赖，但不得据此生成实际 SQL 或宣称完整可用。

后续独立切片需要同时完成：

1. 按用途生成 List/Detail/Selection 独立 VO 与显式 SQL 投影；不能只在序列化时隐藏整行数据。共用 Query 的筛选、范围、count 和 export 不变。
2. 原整 entity 回传继续由固定页归一器接住，保持只读回显白名单、真实父/项目一致、未知字段拒绝及 Optional 的省略/null 区别。写事务与文件/Multi 观察器不能使用窄读实体替代已锁完整记录。
3. Agent 响应 schema 跟随真实用途 VO；写入 schema 不因只读输出变化放宽。
4. 详情保存通知只合并当前列表允许属性或重读当前页，不能把详情整对象灌入 List 缓存。
5. 对真实生成 SQL、原 Vue 消费、HTTP/MCP 和 MySQL 分别验证，再记录实际性能基线。本阶段未运行数据库、HBuilderX 或浏览器。

## 验证

在仓库内以 dotnet test backend/tests/Metarill.Generation.Tests --filter FullyQualifiedName~ReadProjectionCompilationTests 执行。

测试覆盖用途隔离、原 flag 优先级、精确入站名称、关系名和独立目标父、实际原控件覆盖、原按钮嵌套规则、未知能力拒绝；另执行真实生成项目 C# build，且明确断言当前 VO 尚未接入本 IR。

## 固定平台自定义控件读取子集

FixedCustomControlReadCompilation 仅接入 IR 的原详情 CustomControl 分支，仍不接 VO/SQL 或完整包能力门禁。标签必须为精确值，不能夹带属性；宿主表与来源列固定，不按任意 JSON 属性、字符串字段名或别页读取来扩展。

| 精确组件 | 固定宿主 | 当前页必需可读物理字段 |
| --- | --- | --- |
| yzp-button-visible-rule-editor | config_button.visible_rule_json | visible_rule_json |
| yzp-table-storage-mode | config_table.is_rongyu | id、is_rongyu、is_view、is_function_table |
| yzp-control-detail-editor | config_column.control_detail | control_detail、control_kind、name；config_column_id 必须未声明 |
| yzp-control-detail-editor | config_page_column.control_detail | control_detail、config_column_id、name；control_kind 必须未声明 |

两种 control_detail 宿主对应原组件不同继承分支：字段页直接读取 controlKind，页面字段页通过 configColumnId 调用现有受权字段元数据接口。不存在的属性保持原 undefined 语义，不伪造 null 列。若宿主新增了会改变此分支的属性，固定合同先报 Gap，等待新增证明。

原组件收到完整 entity 后透传、拷贝并仅更改自身字段，不代表其读取授权需要整个 Readable。真实 Vue 源码消费者比较窄实体与完整实体的渲染、计算属性、原事件；验证无关值在完整事件中保留，但不进入窄读取计划。网络响应和宿主控件节点受控，不宣称真实浏览器或后端写入验收。

缺失、不可读、虚拟/联接、数组或不符合固定类型的必需字段仍 Gap；未知标签、属性、宿主和动态分支仍 Gap。列表页级 CustomControl 没有此详情合同，仍明确拒绝。Join/MultiCheckBoxDatabase 原缺口及 FullApplication 自定义组件门禁没有放宽。

controlKind/controlDetail 页级覆盖精确保持原长度规则：长度为零才继承，空格不回落。列表空格 kind 按原普通显示，详情仍按表级 kind；空格自定义标签明确 Gap，空格常量明细原样进入派生计划。


Vue3 内部适配仅将 yzp-control-detail-editor 的 emitEntity 中旧 this.$set 替换为响应式对象直接赋值。保留原实体引用、DOM、props、receive 事件及“值未变则不回传”的行为；真实完整组件探针使用标准 Vue3，不提供虚假的 $set 宿主兼容层。is_rongyu 固定为 int32 的 0/1；is_view/is_function_table 经原 Number 消费允许 int32/bool，四种 bool 组合以真实组件证明。


## 2026-10-25 B70：固定维护当前读联接裁剪

B70仅接维护当前读的内部联接选择，不是上文List／Detail／Selection IR接入VO及SQL。代码盘点仍只有该IR定义，主生成器未调用其ForPage；不能据B68／B70宣布按用途页面读取已完整落地。实际响应字段若需改变，仍按既定接口兼容要求先给证据与影响并确认。 最终数据585、文件136、API83、相关生成11及租约15通过；前轮扩大41独立保留。215产物两轮稳定、原546资源不变，正式全局新库、原浏览器／设备及同条件性能仍未完成。范围、证据、升级与回退见[维护联接专项](maintenance-join-projection.md)，冻结 `611a68a0cb77`。


## 2026-10-25 B71：固定事件来源锁读列投影

B71只补内部目标事件的来源锁读列投影；上文List／Detail／Selection用途IR仍未接独立VO及SQL，不能把本批当作页面用途接线或性能总验收。 最终数据600、文件136、API83、相关生成11及租约15通过，215产物两轮稳定、原546资源不变；正式全局矩阵、原浏览器／设备与同条件性能继续。具体合同及更新回退见[来源锁读专项](maintenance-source-projection.md)，冻结 `4430e23c08d5`。


## 2026-10-28 B97：原单 VO 的兼容读取列投影

B97 独立通过 `PublishedReadProjectionCompilation` 缩小普通实体纯读取，保留整个原 Readable 并集及展示依赖；没有调用本文件的用途 IR，也没有生成独立用途 VO。三个读取入口接线、FreeSql SQL 编译及原 VO 等价已验证；写锁完整实体、非冗余固定计划、凭据脱敏、前端和现有响应均保持。最终生成 224 通过／4 实库跳过，API 53 通过／46 实库跳过；191 受管文件与清单两轮稳定、24 份手写 partial 不变，17 个服务仅变更 40 行纯读取。不得据此宣布数据库性能或 V09 整体完成；具体失败记录、更新回退和边界见 [B97 专项](published-read-projection.md)。
