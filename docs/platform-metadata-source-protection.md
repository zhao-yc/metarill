# 平台元数据重建的已发布声明保护

平台元数据的完整重建以仓库内输入为准。本片不连接数据库、不发布配置、不初始化种子，也不修改页面操作、类型规则或权限。

## 问题与单一源头

`4fff32d` 的 canonical 已包含七个字段的固定关联声明、两个页面字段的合法 Custom 标签及四条索引工作台索引，但统一源头未完整保存这些值。完整重建曾丢弃这些已发布声明。本片将七个字段的精确 `controlDetail`／`linkConfigTableId`／`linkConfigPageId` 及两个页面覆盖写回 `platform-source.json`，并在原索引构建链恢复四条声明；不是在生成后附加修改产物。

四条索引为 `ix_index_table`、`ix_index_project`、`ix_index_column_parent`、`ix_index_column_project`，保留已发布索引 ID `2200000000000000900/903/906/910` 与九个字段 ID。`identifiers.json` 补齐这些既有 ID 的十三个稳定键，不分配或更改它们。原关系的唯一父 owner 声明、现有目标表／页面以及两个 `yzp-button-visible-rule-editor` 标签均保持原已发布值；没有借原 Java 的错误目标反向覆盖 canonical。

## 自包含的结构参考

旧 builder 还会从本机旧 YZP 的可选 schema SQL 文件补字段。没有该目录时，会少 113 个字段、131 个页面字段，另有共同记录的标签、类型和排序发生变化，不能只冻结少数关联后称为完整重建。

`platform-schema-reference.json` 固化这段导入实际使用的结构信息：16 张原允许表的声明，其中 9 张存在原结构文件，共解析出 367 条按原顺序排列的字段声明。367 是原解析声明总数，113 是无参考时缺失的字段差额，两者不是同一个统计。每条只含字段名、物理类型和长度，不含 SQL 正文、行数据、连接配置或凭据。

builder 在原导入位置消费受控 JSON，沿用原 `addColumn`、排除字段及 `standard` 默认策略。移除对外部目录和 `YZP_REFERENCE_ROOT` 的隐式依赖，不改变逻辑类型映射或生成能力。后续平台结构需要显式更新仓库输入，再完整重建；不从开发者电脑上的旧仓库悄悄补列。

## 验证与跟进

执行 `node scripts/build-platform-metadata.mjs`，然后 `node --test tools/metadata/platform-builder.test.mjs` 与 `node tools/metadata/derive-files.mjs --check`。测试在新临时目录运行实际 builder，将旧参考路径指向不存在的位置，核对完整目录语义、两轮 UTF-8/LF 字节与稳定 ID。冻结声明摘要覆盖七列、两个页面覆盖、四索引九字段；实际负变体覆盖父 owner 丢失、旧 Custom 标签、索引丢失、结构参考漏列以及结构字段缺名称／类型；共六项测试、零跳过。

本片不含三条待优化的安全表关联索引。无外部参考时，生成的 23 表、649 字段、20 页面、662 页面字段与本片前 canonical 语义完全相同。canonical 存在六列属性键位置的十八行序列化差异，值和数组顺序未变；两轮重建字节稳定。索引工作台／结构预览和实际数据库状态分别验收，源码声明不能冒充已执行 DDL。