# 元数据创建联动与历史缺口

普通元数据更新不能顺带把刻意未开放、已删除或尚未配置的列加到既有页面。本片修复真实发布发现的联动扩大：修改一个`config_column.control_detail`曾额外增加三个既有页面字段；本片不操作这些live记录，不自动清理或补历史数据。

## 本片边界

- Table普通Update不新增默认系统列、默认页或缺失页字段；已提交table_name的固定派生传播保留。
- Column普通Update只保留原类型、归属、引用校验及实际名称/排序派生传播，不补历史页字段。Column Create只在各页添加本次新列，不把同表其它缺失或软删映射补回来。
- Page普通Update不补页字段。当前C# Page/Table Create正例暂保；页面创建仍只处理本页，引用/派生校验和所有联动继续加入同一事务。

所有旧归属和已有引用校验保持，不以删除校验来避免新增行。逻辑类型局部更新兼容规则、权限、UI、必填、数据库结构和生成产物均不修改；没有性能对照结果。

## 原Java事实与后续差异

只读原YZP `ConfigColumnServiceImpl:50–52`仅在afterAdd调用createForNewColumn；该方法只为本次新列建映射，不修其他列。`ConfigTableServiceImpl:48–49`仅afterAdd调用createFromTable。`ConfigPageServiceImpl:27–75`的显式createFromTable在保存页面后初始化全部字段；普通Page Service未覆盖afterAdd/afterSave来自动建字段。`ConfigTableController:228–231`的原create_page按钮复用该显式入口。

当前C#普通Page Create自动补本页字段、Table Create存储默认系统列，属于暂态创建链，本片为了不同时改create_page/复制依赖而保留，不能宣称完全等同原Java。后续应另片恢复原显式createFromTable链。原新增列页数>2默认隐藏、join只读、sort_no默认99/必填的默认值差异也列入后续恢复，本片不顺带改变UI默认。

## 验证

离线使用真实SqlRowStore/ConfigurationMutationObserver及内存命令适配器，覆盖三类Update有洞与软删洞、新列只补自身、现C# Table/Page Create、源对象与映射尾部异常整体回滚；不把这些测试当MySQL锁验收。新增一个Server200 metarill_test的随机元数据项目用例，复用OwnershipFixture，先核host/uuid/schema，再验证同样更新边界、新列跨页映射、名称/排序传播与真实事务回滚，最后仅清理并核验自有精确ID。该测试不DDL、seed或修改平台发布现场。

本片冻结验证：相关离线57/57通过、零跳过；上述唯一真实MySQL用例1/1通过、零跳过。两随机项目的21个精确自有ID逐项清理后均为0；平台发布现场及其三条意外新增记录未操作。实库证据只验证该随机SqlRowStore/Observer流程，不代替HTTP发布、并发性能或原Java完整创建默认验收。
