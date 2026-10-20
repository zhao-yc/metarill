# 安全基础物理模型派生

`backend/metadata/security.json` 是从已提交的 `platform.json` 派生出的平台兼容完整物理定义。固定六表为 `yz_project`、`yz_admin_type`、`yz_manager`、`yz_role`、`yz_role_permission`、`sys_session`，不是只含最低认证字段的精简 schema。当前包含 128 个物理字段、5 个既有索引与 8 条索引字段声明；全部沿用源头稳定 ID，不修改 `platform.json` 或 `identifiers.json`。

此文件只是后续独立宿主显式结构安装的准备输入。本片未接入 Host、认证、账号初始化、三个手写 Service 守卫或打包器；没有连接或修改数据库，没有下发安全协调运行时，也没有关闭角色运行时能力缺口。主应用与平台的既有结构安装流程不因新增此文件自动改变。

## 源头与物理边界

派生器固定读取当前检出目录的 `backend/metadata/platform.json`，只能输出 `backend/metadata/security.json`，不接收外部路径、表名列表或 SQL。源平台可以包含其它表，但输出闭包只允许上述固定表名与稳定表 ID；替换成 Agent 或其它表不能通过验证。六表的非虚拟字段全部保留，必要主键、软删除、审计、身份关联、摘要和会话字段及现有关键索引另外按稳定定义核验。未来增加合法物理列或索引会使 `--check` 发现产物漂移；删除必要字段、重分配关键 ID 或改变必要索引会直接拒绝派生，需审查相应合同后处理，不能只重写文件掩盖。

输出采用显式物理属性白名单：表 ID/表名/实体类型；列 ID/表归属/列名/逻辑类型/长度/非空/虚拟标志/精度/小数位/物理默认值；索引 ID/表归属/名称/唯一性/类型/启用，以及完整索引字段 ID、归属、列、排序和方向。精度、小数位和虚拟标志的缺省值与 C# 元数据模型一致。显示名称、页面、菜单、控件、关联声明、任意扩展、查询 SQL、连接配置及 Agent 配置均不进入输出。排序固定，使用 UTF-8 无 BOM、LF 和末尾换行。

完整平台物理兼容要求保留 `mysql_conn_str`、`db_url`、`db_username`、`db_password`、`password` 等**列结构**，不包含任何实际行值或连接设置。连接、口令、盐、令牌等敏感列的非空物理默认值会被拒绝，错误消息也不回显原值。不能把本模型误称为连接配置备份、密码种子或可直接运行的认证目录。会话表保留原 `grant_id` 物理列，不因此引入 Agent 表或控制面。

## 生成与校验

```powershell
node tools/metadata/derive-security.mjs
node tools/metadata/derive-security.mjs --check
node --test tools/metadata/derive-security.test.mjs
```

`--check` 只比较文件，不修改产物。现有 CI 的后端矩阵增加一项派生合同测试和提交字节检查。修改安全表物理源后应先审查变更，再派生、检查差异并运行离线组合回归；本命令不执行 DDL 或初始化数据。

## 合成与碰撞

继续使用现有 `MetadataCatalogComposition.MergePhysical` 严格规则，不放宽 Core。业务模型、文件模型与安全模型可以先后显式合成；基础模型没有页面。同名同 ID 且**全部列物理定义、索引及完整索引字段定义相同**的纯物理副本才去重。列缺失、类型、长度、非空、默认值、索引 ID、唯一性、字段顺序或方向漂移均拒绝；表、列、索引及索引字段跨类别借用 ID 也拒绝。输入保持不变。

若应用已用页面开放同名安全表，即使物理定义一致，仍会按现有规则拒绝。带平台安全 CRUD 页的模型不能借本片直接合并通过；如何证明并下发安全页面属于后续 Host 片，不能通过删除检查或宽松合并绕过。

## 验证边界

Node 回归实际运行派生 CLI，覆盖六表缺失/替换、必要字段与索引漂移、重复或跨类别 ID、跨表索引、敏感默认值、受控输出、两轮字节稳定和检查不写入。C# 回归调用真实 `MetadataValidation`、`MySqlSchemaPlanner.Compile` 和 `MergePhysical`，对照平台六表的完整结构计划，并验证与 demo/files 合成和冲突拒绝。它们都是离线验证，不代表真实 MySQL 安装、登录、撤权并发或独立 Host 验收。

本片实际结果：Node 11/11；C# 新增 17 项及既有组合 4 项，共 21/21，零跳过，`-warnaserror` 零警告。C# 报告位于本地 `.local/security-physical-metadata-results/security-metadata-composition.trx`，日志不提交。
