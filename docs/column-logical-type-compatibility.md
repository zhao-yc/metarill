# 字段逻辑类型与局部更新兼容

固定平台字段配置页继续保留原数据类型控件、必填标记和数据库非空元数据，不修改页面布局、`isMust`、`isRequired` 或 `isDbNotNull`。

## 旧空物理类型的更新

平台项目 `1608097412978577409` 的 `config_column` 表 `6217714881724416`、页面 `6217715532627968`，仅在生成的 Update 中识别稳定字段 `column_type/1670726649369513985` 和 `logical_type/2200000000000000129`。实体先按原身份和范围 `FOR UPDATE` 读取，在 DTO 赋值及手写钩子前捕获两个类型旧值。

仅当原物理值严格等于空字符串、原逻辑类型有效、请求省略两个类型字段，且最终两值与原值完全一致时，允许保存其他字段。原值为 null 或空白、非法逻辑类型、显式提交任一类型字段、钩子改变任一类型均不适用。创建、复制、其他页面、其他字段的必填校验不变；不能用此分支主动清空物理类型。原整实体请求若显式带回空类型，仍按原必填拒绝。

捕获与比较均是局部内存操作，没有新增 SQL、生命周期接口或写后范围重读。既有事务、身份、归属和观察器顺序不变；不能据此宣称性能已实测。

## 元数据类型规则

`MetadataColumnTypePolicy` 管理元数据存储规则，接受既有 `int64/int32/string/decimal/date/time/datetime/bool/json/binary`，并接受 C# 生成器原已使用的 `double`。无类型字段提交的更新必须保持已有有效逻辑类型，不再根据空物理值重推为 string，也不执行无效果的逻辑类型 UPDATE。已有逻辑类型非法仍明确拒绝。

明确提交物理类型时必须有已知映射；未同时提交逻辑类型时由该映射归一。明确提交合法逻辑类型时以其为准，但不能让同时提交的未知物理类型通过。原表单的物理 int 表示整数；仅从已知物理类型推导时，继续按原命名规则将 `id/*_id` 归一为 int64、`sort_no` 归一为 int32。显式逻辑类型及无类型编辑的已有值若与这些命名规则冲突，仍明确拒绝，不能静默改写。未知物理类型先拒绝，不能借标识或排序字段名绕过。创建时只有基础设施入口确实省略逻辑类型才可从已知物理值推导；原生成 Service 的创建必填规则继续有效。

已知物理映射包括 bigint/int64、int/integer/tinyint/smallint/int32、decimal/numeric/double/float、nvarchar/varchar/char/nchar/text/tinytext/mediumtext/longtext/string、date/time/datetime/timestamp、bool/boolean、json、blob/binary；保留物理 double/float 到逻辑 decimal 的既有映射。空值、未知类型、point、varbinary 的明确类型编辑拒绝，不默认转换为 string。无关字段更新不替用户迁移已有物理类型。

逻辑类型合法不等于生成能力完成。time、binary 的已有元数据可以保持；当前 C# `TypeFor` 仍明确拒绝生成这两种类型。本切片没有扩展数据库或代码生成能力。

## 验证和接入

离线回归执行实际生成 DTO、Service、Mapper 及事务边界，ORM 执行由明确代理提供；另一组执行真实 SqlRowStore/ConfigurationMutationObserver，数据库命令由内存适配器提供。覆盖空物理值的 decimal/int64/double/time/binary 保持、固定身份边界、原 UI 必填、显式类型、创建/复制、未知映射、旧 int 标识创建推导、显式逻辑命名冲突和尾部异常回滚。这些测试不证明真实 MySQL 锁与并发行为。

源头集成后应正规重新生成平台，再使用真实身份、原元数据生命周期接口重验已授权的窄更新。本任务不修改生成产物、不访问实库、不发布或回填任何 live 元数据；本任务不重新执行原先失败的发布。
