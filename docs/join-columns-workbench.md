# 原联接字段工作台的C#适配

沿用现有 `pages/custom/add_join_columns.vue`，页面、样式、选择、标签与确认交互未改。原页面的两个JSON接口现由 `MetadataWorkbenchBusiness` 接线：

- `POST /api/admin/custom/config_table/join_column_candidates`：请求 `{configTableId}`，返回原表信息与 `groups`；分组包含关系源ID/名称/前缀、目标表信息、诊断及字段候选。
- `POST /api/admin/custom/config_table/save_join_columns`：请求 `{configTableId,selections:[{sourceColumnId,targetColumnId}]}`，返回原 `createdCount`、`createdColumns`。

两入口只允许真实网页登录管理员，项目来自服务端认证上下文；不接受客户端项目、连接或SQL作为执行参数。匿名、普通账号及Agent身份在创建数据库事务前拒绝。配置聚合加入现有项目锁，读取当前表、字段、目标表与页面后计算候选；不回退读取其它项目的同名目标。

## 原配置规则与事务

按原 `_id` 关系字段分组，排除已有联接源；目标依次解析 `joinTable`、List/SelectPage/MultiSelectPage声明及字段前缀。同项目List目标ID失效时仍可按已声明表名解析，ID与表名同时指向不同有效表则明确拒绝。

候选保留原字段排序，系统审计字段、Title/Tab、关系自身ID、已存在字段/完整路径和原 `nameField` 辅助列不再重复添加。原 `yz_project_id` 候选仍保留，没有为迁移删减原字段集合。控件明细只解析目标及名称定义，不执行其中的SQL条件。目标不存在时返回原空字段分组及诊断文案。

保存时重新读取并计算候选，先验证全部选择、重复键、别名冲突与整数排序空间，再创建字段。目标字段类型、长度、精度和控件声明沿用；保存 `isJoinColumn=1`、`isVirtualColumn=0`、原 `prefix.field` 完整路径及基础默认值。已有 `ConfigurationMutationObserver` 在同一个事务里验证字段并补各原页面的字段及缓存，不直接手写生成Entity/Mapper/Service，也不执行业务DDL、同步结构或代码生成。

非法或过时选择使整批拒绝，不能静默保存合法子集；并发重复保存受同一项目锁保护，后到请求按当前候选重检。整批事务失败会同时回滚已写字段和自动页面映射。

## 验证与边界

最终元数据工程174通过、22跳过、零失败；其中新增规则与查库前身份测试30项。22项未提供私有实库配置而跳过，不计实库验收。

最终真实平台登录、HTTP/MySQL专项3/3、零跳过：原候选和两列保存/页面映射/重复拒绝，非法整批/外项目/重复JSON拒绝，以及第一列和页面映射写入后的第二列ID尾部故障完整回滚。平台结构快照前后保持。各重跑不累计为新增用例；首轮夹具重复创建默认name字段的2失败、次轮误漏原yz_project_id候选的1失败保留，未通过删除原字段迎合测试。

成功轮次按自有精确ID清理，失败轮次按保留清单逐行核对完整快照、项目及专属会话后事务清理；另起独立只读连接复核全部精确ID、会话及项目范围均零残留。未改变既有项目配置、数据权限或业务表结构。

本批没有HBuilderX或浏览器实测；前端未改不等于页面交互已验收。联接配置可保存不代表非冗余读计划、冗余传播、维护回填、派生筛选/排序/统计及完整独立包已完成；这些仍按主框架审计继续交付。


后续已统一[非冗余字段存储归属](nonredundant-storage.md)，保存的联接声明仍是原元数据结构，不通过改写isVirtualColumn掩盖存储模式。非冗余联接不会被反建成真实列；完整派生查询仍待实现，生成入口对此保留写前拒绝。
