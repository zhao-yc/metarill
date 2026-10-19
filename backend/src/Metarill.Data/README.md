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

## 名称JSON数组成员筛选

`Queries.JsonArrayNameQuery.ApplyJsonArrayAnyLike(session, query, entity => entity.<已编译属性>, keyword)` 只接受已映射实体的直接字符串属性。生成器必须先证明该属性是本页开放的固定名称辅助存储列；本接口不接收HTTP列名、SQL或别名。返回值在原query上附加条件，项目、父范围、软删除、排序及分页由原查询保留；Count、列表和导出应复用同一受限Query。

关键词是**字面子串**：`!`、`%`、`_` 转义后包裹 `%`，通过独立 `DbParameter` 绑定。列引用及真实别名由 [FreeSql公开ExpressionCall合同](https://freesql.net/guide/expression-function.html) 提供，代码不读取ORM私有表结构，也不猜默认别名。

MySQL实现使用 `JSON_SEARCH` 搜索解码后的string成员，避免对包含 `\u`/引号/反斜杠转义的原始JSON文本执行Contains。SQL NULL、空文本、非法JSON、非数组和空数组均不匹配；顶层数值/bool/null元素不会当名称。含非空嵌套数组或对象（`JSON_DEPTH>2`）的异常文档**整行不匹配，即使同时含合法顶层string**；混合空容器不会增加名称。正常Names写入合同仍是纯string数组，本适配器不修复或重写异常历史值。

[MySQL 5.7 JSON搜索文档](https://docs.oracle.com/cd/E17952_01/mysql-5.7-en/json-search-functions.html) 与 [JSON属性文档](https://docs.oracle.com/cd/E17952_01/mysql-5.7-en/json-attribute-functions.html) 已列出所用函数，未引入要求8.0的JSON_TABLE。实际隔离库仅验证Server-200的 **MySQL 8.4.11**；5.7仅核实语法所需函数，未实测，不能据此宣布版本验收。其他provider明确拒绝，不借用MySQL SQL。

新增真实测试读取私有 `Metarill.BusinessDatabase`，仅允许解析到Server-200的`metarill_business_test`。由元数据SchemaService创建唯一随机表；测试完成先核对结构指纹、完整受测行、触发器和外部FK，再清理本次表。离线ToSql与未配置时skip不代表真实SQL执行通过；此Data切片不代表生成Query的名称筛选接线已经完成。
