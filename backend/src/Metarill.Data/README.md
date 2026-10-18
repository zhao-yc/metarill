# 数据访问与结构同步

- `DatabaseSession` 使用 FreeSql + MySqlConnector；建议作为单例注册，当前异步调用链中的事务通过 `AsyncLocal` 隔离。所有业务 Mapper 使用 `Select/Insert/Update/Delete` 帮助方法，直接使用 `Orm` 不会自动附加事务。
- `InTransactionAsync` 的内层调用加入同一事务；内层失败即使被外层捕获，外层仍只能回滚。事务内按顺序调用数据库命令，不在同一事务上并行执行。
- 必须同时配置连接串与 `ExpectedDatabase`，禁止系统数据库和启动自动改表。Provider 为后续数据库适配边界；首期只有 MySQL 实现，不能把品牌列表当成验收结果。
- `SchemaService.PreviewAsync` 仅读取结构并生成计划；`ApplyAsync` 只接受当前实例生成的原始计划，30 分钟后失效，进程重启或使用后必须重新预览。计划同时绑定数据库服务器 UUID、库名和受管辖结构摘要。
- 只处理元数据实体表，视图与功能表跳过；必须显式定义非空 `int64 id`。标准字段全部由元数据声明，不隐式增加。未知逻辑类型、非法标识符、破坏性缩窄、必填字段无回填、同名索引结构变化均拒绝。
- `string` 的空长度默认 255，`max/longtext` 映射长文本。首期结构默认值只接受安全常量，不接受表达式或含反斜杠字符串；业务默认值优先放 Service。
- 元数据索引使用 `configIndexes: {id,configTableId,indexName,isUnique}` 和 `configIndexColumns: {configIndexId,configColumnId,sortNo}`；支持组合及唯一索引，不删除未声明索引。唯一数据冲突由实际数据库明确失败。
- MySQL DDL 会隐式提交。失败结果保留已执行数量，不能视为整体回滚。宿主应注入 `FileSchemaChangeJournal`，把预览指令和结果追加到配置的 JSONL 审计文件；默认 `History` 仅为最近 1000 条内存记录。
- 专用真实测试通过 `METARILL_TEST_SETTINGS` 指向不提交的设置 JSON，结构为 `Metarill.Database.{Provider,ConnectionString,ExpectedDatabase}`，只接受 `metarill_test/metarill_business_test`。测试仅创建、清理随机 `mt_test_` 表；未配置时明确跳过。额外设置 `METARILL_PLATFORM_METADATA` 可执行平台元数据的只读预览。
