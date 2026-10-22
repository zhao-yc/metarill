# Metarill 实施进度表

更新日期：2026-10-22。首次盘点的产品代码基线：`ef946fd`，分支：`codex/initial-platform`。

当前阶段：C# 主干和 MySQL 普通业务闭环已跑通，正在补齐复杂功能与完成原前端、独立应用和首版验收；尚未达到完整替代 YZP 的发布标准。本表是后续任务的统一进度入口，详细过程、失败和证据继续保留在专项文档中。

## 标记及维护规则

- **✅ 已完成**：仅代表该行明确范围已经实现且有对应验证；不代表整模块、所有配置组合或所有平台通过。
- **🟡 部分完成**：有可用子集，仍有明确功能缺口；在“下一步／剩余范围”写明未完成部分。
- **⬜ 待开发**：尚未完成该项功能实现或接线。
- **🧪 待验收**：已有实现或分批验证，但该行要求的完整验收尚未通过。
- **⏳ 待确认／待配置**：依赖已提出的行为决定或外部配置；不影响其他可独立推进的工作。

后续每完成一项，必须在**同一个任务／提交**内更新本表：保留编号，将状态改为“✅ 已完成”，写入实际完成日期、对应代码提交或冻结基线，以及验证依据。部分完成只更新真实覆盖范围，不能直接打勾；发现回归则恢复相应未完成状态并链接失败记录。新增任务追加编号，不删除旧行掩盖缺口。不把构建、静态检查、HTTP、浏览器、真机和性能验收混为一项，不按行数计算虚假完成率。

以下“✅”均为既有成果在本次盘点中确认，原完成日期和提交以链接的冻结批次为准，不能将更新日期当作所有功能的首次完成日期。动态 CI 结果须按具体 SHA 重新查询。

## 一、基础架构、平台和生成链路

| 编号 | 状态 | 工作项 | 已完成范围／下一步与剩余范围 | 验收依据／代码基线 |
| --- | --- | --- | --- | --- |
| A01 | ✅ 已完成 | C# 工程与分层 | ASP.NET Core、FreeSql、Service 普通业务及按需 Business；数据库差异集中在基础设施。 | [架构](architecture.md)、[验收台账](acceptance.md) |
| A02 | ✅ 已完成 | 强类型代码生成 | Entity、DTO/VO、Mapper、Service、Controller 及页面生成主链已接通。 | [验收台账](acceptance.md) |
| A03 | ✅ 已完成 | partial 与重复生成保护 | 同程序集生成／手写分离；189 个归属输出、2 份清单及24个手写文件（合计215文件）两轮稳定，手写 partial 保持。仅对应已验模型。 | [主框架审计末尾批次](framework-gap-audit.md)，`b97ce89` |
| A04 | ✅ 已完成 | 菜单与页面归属优化 | 单表树菜单、页面直接归属后台类型，修正原角色权限页关联引用。 | [架构](architecture.md)、[权限引用](platform-permission-references.md) |
| A05 | ✅ 已完成 | 原接口基础兼容 | `/api`、原业务路由、R、分页、字符串 ID、省略与显式 null 的基础合同；复杂功能另列。 | [接口合同](integration-contracts.md)、[原页兼容](original-page-api-compatibility.md) |
| A06 | ✅ 已完成 | MySQL 普通纵向闭环 | 登录→配置表字段→结构预览／执行／核验→生成→普通业务 CRUD 已实际跑通。 | [验收台账](acceptance.md)、[全新安装](fresh-install-acceptance.md) |
| A07 | ✅ 已完成 | 显式结构变更与受管反查 | 已登记表的 HTTP／MCP 反查、差异计划、执行和核验；普通启动不 DDL。 | [结构反查](schema-inspection.md)、[验收台账](acceptance.md) |
| A08 | ✅ 已完成 | 全新库安装基础场景 | 平台与普通独立应用显式安装、重复同步零差异、初始化不重置密码及真实登录；不含下表后台初始化按钮。 | [全新安装](fresh-install-acceptance.md) |
| A09 | ✅ 已完成 | C# 模板版本下拉 | 保留原下拉与事件，切换到 Metarill C# v1 目录；已授权四笔配置修复有逐笔回读。 | [版本目录](template-version-catalog.md)、[前端纠偏](frontend-preservation-correction.md) |
| A10 | ⬜ 待开发 | 平台 CLI 与未登记表反查 | 补按项目反查、未登记表安全目录；已有受管 HTTP／MCP 与独立宿主 inspect 不替代此项。 | [结构反查](schema-inspection.md) |
| A11 | ⬜ 待开发 | 数据库结构反向导入元数据 | 结构→逻辑模型→差异预览→确认→元数据事务保存，不能读取后自动写入。 | [主框架审计·数据库反查](framework-gap-audit.md) |
| A12 | 🟡 部分完成 | 统一字段类型与索引优化 | 已有逻辑类型、索引工作台及专项校验；仍需按完整模型核对 ID／关联类型、排序、重复索引及确认无用途字段。 | [字段类型](column-logical-type-compatibility.md)、[索引工作台](index-workbench.md) |

## 二、原前端、业务功能及关系控件

| 编号 | 状态 | 工作项 | 已完成范围／下一步与剩余范围 | 验收依据／代码基线 |
| --- | --- | --- | --- | --- |
| B01 | ✅ 已完成 | 原列表／详情／导航生成入口 | 默认生成器已恢复原模板、PC／移动分支和导航；移除简化默认模板。这里只标记源头恢复，不代表全页面验收。 | [前端恢复记录](frontend-preservation-correction.md) |
| B02 | ✅ 已完成 | 原普通单表页面浏览器闭环 | 原 PC 分页、筛选排序、必填、保存、复制、删除及移动视口已分批实测；移动视口不等于真机。 | [前端恢复记录](frontend-preservation-correction.md) |
| B03 | ✅ 已完成 | 原账号／角色基础浏览器操作 | 原页面新增、改密、角色回显、旧密码拒绝／新密码登录、逻辑删除及服务器注销已补验。未授设备的普通账号业务页面另验。 | [账号浏览器验收](testing/original-account-browser-acceptance.md)，`1684cb9` 及注销修复批次 |
| B04 | ✅ 已完成 | 主子与多选已证明子集 | 主子 Tab／PC 弹层、type=2 独立多选及冗余 MultiSelectPage 固定子集已有包、接口及分批浏览器证据。 | [独立应用](standalone-application.md)、[多选字段](multi-select-field-contract.md) |
| B05 | ✅ 已完成 | 远程 List 已证明子集 | 原选择器分页、搜索、已选回读、双父范围及 128／105 候选浏览器操作；不含全部多选与超大候选。 | [关系候选](relation-options.md) |
| B06 | ✅ 已完成 | 平台安全写协调固定范围 | 平台账号／角色／后台及权限工作台复用协调器，有实库、RR、回滚证据；独立管理包另列。 | [安全协调器](security-realm-write-coordinator.md)、[RR专项](security-realm-rr-validation.md) |
| B07 | ✅ 已完成 | 文件库基础与同事务引用 | 平台按业务项目定位数据库，独立应用本地文件库、上传／下载、引用阻删与回滚已在固定场景实测。 | [验收台账](acceptance.md)、[全新安装](fresh-install-acceptance.md) |
| B08 | ✅ 已完成 | 普通 Excel 与基础统计 | 原可写列边界、普通导入导出、失败回滚，及物理数值 SUM／原百分比合同已接通；不含所有复杂关系与浏览器下载。 | [统计](list-statistics.md)、[导入错误](import-validation-errors.md)、[Excel边界](cross-table-excel-empty-contract.md) |
| B09 | 🟡 部分完成 | 非冗余关系存储 | 普通固定标量、单值、type=2、物理文件已有；固定非冗余 MultiSelectPage 已完成（B16），selfTree 正在收口（B18／B24）；其他多值、级联、全局及复杂联接继续实施。 | [非冗余存储](nonredundant-storage.md)、[非冗余文件](nonredundant-files.md) |
| B10 | 🟡 部分完成 | 原级联控件完整能力 | selfTree 普通冗余与 crossTable 固定链已有读取、写入、展示、筛选；同页混合和来源单层参与已验。重复来源层位、共享辅助列及复杂父范围组合仍缺。 | [级联同页与必填](cascade-mixed-required-contract.md)，`393dc41`；[来源图](cross-table-source-graph-contract.md)，`b97ce89` |
| B11 | ⬜ 待开发 | 关系名称传播与维护回填 | 补冗余／非冗余混合名称传播、目标更新／删除影响和维护回填，验证事务与并发。 | [非冗余存储](nonredundant-storage.md)、[主框架审计](framework-gap-audit.md) |
| B12 | 🟡 部分完成 | 完整页面配置能力 | 补齐并验收详情分组、复杂条件显示／只读、完整表单规则及配置按钮；不能以原组件存在判为完成。 | [主框架审计·列表详情](framework-gap-audit.md) |
| B13 | 🟡 部分完成 | 复杂文件与云存储 | 基础文件已接；补复杂关系／派生文件、全部配置组合及 S3 等云存储完整矩阵。 | [非冗余文件](nonredundant-files.md)、[原文件管理](original-file-management.md)、[主框架审计](framework-gap-audit.md) |
| B14 | ⏳ 待确认 | 跨表级联非空路径 Excel | 空单元格、可写列边界已完成；非空路径的旧回退语义与无损往返存在冲突，等待选择后接通完整导入导出。 | [待确认策略](cross-table-excel-policy-decision.md)、[已完成空值范围](cross-table-excel-empty-contract.md) |
| B15 | ⏳ 待配置 | Tiny Cloud 实际富文本 | 已按自有 Tiny Cloud 保留原交互并接入配置；API key 按用户要求暂空。配置后验真实加载、编辑、保存和上传。 | [Tiny Cloud](tiny-cloud.md) |

### 本轮复杂关系细分（B09／B10／B11）

以下条目按功能及验证层次拆分，完成固定子集不会将汇总行整项打勾。

| 编号 | 状态 | 工作项 | 已完成范围／下一步与剩余范围 | 验收依据／代码基线 |
| --- | --- | --- | --- | --- |
| B16 | ✅ 已完成 | 非冗余 MultiSelectPage 固定后端／生成链路 | 固定字符串名称、显式／自动数组、分页前筛选、CRUD／当前回显、Excel、原 Vue 协议、Full 包与更新保护通过；仅此后端／生成固定子集，不含 B17 和 B23。 | [多选专项](nonredundant-multi-select.md)，2026-10-22；源码快照 `2de892abb36b` |
| B17 | ⬜ 待开发 | 其他多值控件与多选派生名称 | 通用 MultiCheckBoxDatabase、派生／非字符串多选名称、复杂联接和未证明覆盖；不以 B16 代替。 | [多选剩余范围](nonredundant-multi-select.md) |
| B18 | 🟡 部分完成 | 非冗余 selfTree | 固定后端的 ID 存储、实时名称／路径、筛选／统计／Excel、当前读与同表图已验；固定完整包子集已完成 B24。来源父范围、混合控件及原浏览器／正式宿主组合继续补齐。 | [自树专项](nonredundant-self-tree.md)、[原树读取](cascade-tree-read-contract.md) |
| B19 | 🟡 部分完成 | crossTable 剩余组合与非冗余链 | 冗余固定链及来源单层参与已验；非冗余、重复来源层位、共享辅助列和复杂父范围仍缺，Excel 策略另按 B14。 | [来源图](cross-table-source-graph-contract.md)、[主框架审计](framework-gap-audit.md) |
| B20 | 🟡 部分完成 | 复杂联接与全局／派生名称 | 已有固定单值多级路径；补与多值／级联混用、全局目标、复杂派生名称及各读取用途一致性。 | [非冗余存储](nonredundant-storage.md) |
| B21 | ⬜ 待开发 | 名称变更与混合存储传播 | 从已发布关系图传播目标改名、外键变化与删除，涵盖非冗余中间节点和冗余消费者的同事务更新。 | [主框架审计](framework-gap-audit.md) |
| B22 | ⬜ 待开发 | 原维护入口回填 | 按原确认与接口接回联接字段回填，校验元数据／物理列、权限、目标库、并发和整批回滚。 | [主框架审计](framework-gap-audit.md) |
| B23 | 🧪 待验收 | 复杂关系原浏览器与正式宿主组合 | 补本轮各子项的原页面现场操作、正式身份、完整包和专用空库交叉矩阵；Node／固定授权器 HTTP 不替代。 | [前端纠偏](frontend-preservation-correction.md)、[全新安装](fresh-install-acceptance.md) |
| B24 | ✅ 已完成 | 固定非冗余 selfTree 后端／完整包 | 只存所选ID、图名称／路径、筛选统计、Excel、当前读、同表后端、原Vue、独立目标Full包及手写保护已验；仅本固定范围，B18／B23其它组合继续。 | [自树专项](nonredundant-self-tree.md)，2026-10-22；冻结源码 `003b18c2818c` |

## 三、独立应用、初始化、助手与 Agent

| 编号 | 状态 | 工作项 | 已完成范围／下一步与剩余范围 | 验收依据／代码基线 |
| --- | --- | --- | --- | --- |
| C01 | ✅ 已完成 | 普通完整应用独立运行 | 普通受支持项目随包提供后端、原 uni-app 宿主及基础模块；有独立构建、显式安装、CRUD 和文件闭环。复杂未支持用途继续拒绝。 | [独立应用](standalone-application.md)、[全新安装](fresh-install-acceptance.md) |
| C02 | ✅ 已完成 | 独立应用基础正式 Web 认证 | 数据库账号／持久会话、登录权限、改密、注销、重启后会话及禁用／撤权已在固定场景实测；不等于完整管理 CRUD。 | [正式Web认证](standalone-web-authentication.md) |
| C03 | ✅ 已完成 | 本地生成更新与恢复 | 三种更新范围、preview／apply／recover、归属与摘要校验、手写／配置保护和失败恢复已验证。 | [本地更新](local-generation-update.md) |
| C04 | ✅ 已完成 | 原首页三按钮到真实工程闭环 | 原交互→队列→本机受控更新→回执已分批浏览器实证；额外迟到／换身份场景另验。 | [在线助手浏览器验收](testing/online-helper-browser-acceptance.md) |
| C05 | 🟡 部分完成 | 独立项目完整管理模块 | 平台已有安全协调、可信管理模块及实库证据；仍需完整下发三个手写 Service 扩展、工作台和相关自定义组件并验收，整体运行时门槛未解除。 | [独立应用](standalone-application.md)、[可信管理实库](testing/trusted-management-runtime-mysql.md) |
| C06 | ⏳ 待确认 | 原后台初始化按钮 | 同库 Business 与离线回归已准备；账号 ID 保留、同名冲突、重置与会话规则待确认，HTTP／DI 尚未启用。 | [初始化行为决定](admin-initialization-policy-decision.md) |
| C07 | ⬜ 待开发 | 平台到业务库后台初始化 | 按已确认行为接入真实目标库与原按钮，验证身份、事务、重复初始化及会话；不以同库准备代替跨业务库接线。 | [初始化范围](admin-initialization-policy-decision.md) |
| C08 | 🟡 部分完成 | 完整 MCP／Agent 能力 | 平台核心接口及授权已有分批验证；完整 Agent 宿主、浏览器授权、独立应用 Agent 下发与完整功能矩阵仍未收口。 | [验收台账](acceptance.md)、[Agent CLI](../tools/metarill-agent-cli/README.md) |
| C09 | 🧪 待验收 | 助手与管理模块剩余原页场景 | 补原助手迟到／身份切换反例、管理工作台及新增组合的现场浏览器验收。既有三按钮闭环不重复列为未实现。 | [助手验收](testing/online-helper-browser-acceptance.md)、[管理入口](testing/trusted-management-browser-entry.md) |

## 四、MySQL 首版验收与交付门槛

| 编号 | 状态 | 工作项 | 已完成范围／下一步与剩余范围 | 验收依据／代码基线 |
| --- | --- | --- | --- | --- |
| V01 | 🧪 待验收 | 原前端完整保真与功能回归 | 已恢复默认源头并有分批实测；仍须按原页面、PC／移动布局、条件规则、按钮、关系、文件逐项对照。 | [前端纠偏](frontend-preservation-correction.md)、[前端验收](frontend-acceptance.md) |
| V02 | 🧪 待验收 | 复杂控件与大候选浏览器 | 补超过 5000 候选、多选 List、复杂关系、新页面打开方式及其余父范围的真实界面操作。 | [关系候选](relation-options.md)、[主框架审计](framework-gap-audit.md) |
| V03 | 🧪 待验收 | 文件与 Excel 真实浏览器全链 | 补复杂文件、关系导入导出、权限组合与浏览器下载落盘；按钮请求／服务端 XLSX 不替代用户下载结果。 | [前端纠偏](frontend-preservation-correction.md)、[独立应用](standalone-application.md) |
| V04 | 🧪 待验收 | 平台库／业务库组合矩阵 | 已有分库、文件同库事务和空库场景；补全部首期配置组合、初始化、权限、关系与事务并发验收。 | [全新安装](fresh-install-acceptance.md)、[主框架审计](framework-gap-audit.md) |
| V05 | 🧪 待验收 | 小程序 | 原 uni-app 工程实际构建、运行和核心操作验收未完成。 | [前端验收](frontend-acceptance.md) |
| V06 | 🧪 待验收 | Android | 实际构建、真机和核心操作验收未完成；Web 通过不替代。 | [前端验收](frontend-acceptance.md) |
| V07 | 🧪 待验收 | iOS | 实际构建、真机和核心操作验收未完成；Web 通过不替代。 | [前端验收](frontend-acceptance.md) |
| V08 | 🧪 待验收 | Java／C# 同条件性能 | 固定数据、SQL、索引和资源，对照延迟、吞吐及资源占用，稳定退化阻断发布；尚未完成。 | [主框架审计·发布门槛](framework-gap-audit.md) |
| V09 | 🟡 部分完成 | 用途读取计划与 SQL 投影 | 已有用途 IR 及固定读取接线；完整 SQL 投影裁剪与同条件性能仍未完成，不能以 IR 存在代表已优化。 | [读取投影](read-projection-compilation.md)、[主框架审计](framework-gap-audit.md) |
| V10 | 🧪 待验收 | 最终版本整体回归与 CI | 分批测试不相加为全量；最终产品基线须重新完成适用后端、前端、专用空库及远端 CI。盘点时 ef946fd 的两路 CI 仍在运行。 | [验收台账](acceptance.md)、[CI预算修复](testing/standalone-probe-budget.md) |
| V11 | 🧪 待验收 | MySQL 首版总验收 | 完成上列首期缺口和必要确认，核对源码／包／数据库／原界面一致后才能标完成；当前尚未通过。 | [主框架审计](framework-gap-audit.md)、[PR #1](https://github.com/zhao-yc/metarill/pull/1) |

## 五、后续数据库（MySQL 首版完整验收后逐批推进）

每一种数据库均需覆盖平台自举、结构反查／同步、生成项目 CRUD、权限、关系、导入导出及事务并发，并验证平台库与业务库不同的组合。驱动能连接不算产品支持。

| 编号 | 状态 | 工作项 | 下一步／剩余范围 | 验收依据／代码基线 |
| --- | --- | --- | --- | --- |
| D01 | ⬜ 待开发 | PostgreSQL 正式适配 | 补适配与完整验收。 | [架构与数据库边界](architecture.md) |
| D02 | ⬜ 待开发 | SQL Server 正式适配 | 补适配与完整验收。 | [架构与数据库边界](architecture.md) |
| D03 | ⬜ 待开发 | MariaDB 正式适配 | 单独确认差异并验收，不因 MySQL 兼容直接宣布支持。 | [架构与数据库边界](architecture.md) |
| D04 | ⬜ 待开发 | Oracle 正式适配 | 补适配与完整验收。 | [架构与数据库边界](architecture.md) |
| D05 | ⬜ 待开发 | SQLite 正式适配 | 补适配与完整验收。 | [架构与数据库边界](architecture.md) |

## 更新记录

| 日期 | 变更 | 依据 |
| --- | --- | --- |
| 2026-10-22 | B24固定非冗余selfTree完成，B18保持部分完成；最终生成组合44、数据全量185、相邻原Vue7均通过，前端273及HBuilderX通过；各批次不相加。 | [自树专项](nonredundant-self-tree.md) |
| 2026-10-22 | 启动复杂关系模块，拆分 B16—B23；B16固定链路完成：生成全量1195通过；最终纯文本编码修正后数据183、定向14复验通过，不累加批次；其余B17—B23仍未完成。 | [多选专项](nonredundant-multi-select.md) |
| 2026-10-22 | 建立统一进度表；逐项区分已验子集、缺失功能和待验收门槛；在 README 与开发约定登记后续更新要求。 | 只读盘点产品基线 ef946fd、现有专项与验收台账；本次仅文档整理，未新增产品验收结论。 |
