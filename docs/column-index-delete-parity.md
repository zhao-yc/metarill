# 索引引用阻止单列删除

恢复原 V8 的对象规则：`ConfigColumnServiceImpl.beforeDelete` 经 `ConfigColumnRules.validateForDelete` 调用原 `ConfigIndexRules.java:221–235` 的 `assertConfigColumnNotReferenced`。活动的 `config_index_column` 只要按 `config_column_id` 引用该列就拒绝删除；不要求父索引仍存在或处于启用状态。因而禁用索引、父索引软删/缺失但仍有活动子行都拒绝，子行自身已经软删则不阻止。错误使用固定中文提示，不回显索引配置或底层数据库消息。

C# 保护位于 `ConfigurationMutationObserver.DeleteOwnedMetadata` 的同 Session/当前 DbTransaction 生命周期。生成 Service 先经 `ConfigurationMutationGuard` 持真实配置项目锁，软删源行后在提交前通知观察器；观察器复核同事务和项目锁，再通过 `SqlRowStore.ReadForUpdateAsync` 参数化当前读活动索引字段。拒绝会回滚源行软删及同事务先前写入；不再用删列动作替用户软删索引字段。原页面字段从属清理在确认无引用后保留。

## 入口和聚合边界

- 原 HTTP DELETE 调用生成的 `ConfigColumnService.Delete6217715532627968Async`；MCP `PlatformTools.Page` 的 delete 也分派到同一生成 Service，调用相同已注册观察器。没有另设 HTTP/MCP 弱化删除规则。
- 当前字段工作台只有排序、批量创建和明细清空，没有独立删列旁路；后续手写入口若删列仍须沿该 Service/观察器生命周期。`SqlRowStore` 是基础设施原语，直接写它不等于获得对象规则或额外授权。
- 原 `ConfigTableServiceImpl.beforeDelete` 先删本表索引及从属再经 Column Service 逐列删除。C# 整表聚合同样先当前读、软删自有索引字段与索引，再逐列执行相同引用检查；直接从属先按 table/index ID 当前读全部活动行，再逐行证明项目归属；不能先按项目过滤而隐藏跨项目 index/column/child。残存外部或悬空活动引用使整批回滚，不能凭 table 上下文跳过。
- 不执行物理 DDL，不删除业务库索引，不修改元数据开关或生成 partial，不引入联接字段或其它通用引用的新限制。项目锁仍是聚合串行边界，SQL 执行顺序不是 MySQL 实际取锁顺序证明。本片不宣称所有配置入口均已解决中途撤权或任意并发负载。

## 验证

离线用例执行实际 SqlRowStore/观察器/事务代码，覆盖活动、禁用、父软删、缺父、子软删、其它列引用、整表清理顺序与外部/悬空活动引用全回滚，以及错 Session 事务拒绝。检查固定参数、`delete_no IS NULL`、`FOR UPDATE` 和实际命令的同事务引用；内存命令适配器不证明 MySQL 锁/隔离行为。

真实 MySQL 测试源码 `ColumnIndexDeleteMySqlTests` 复用真实 TestServer、认证、生成 Service 和索引工作台，在两个随机元数据项目下建立已登记根表/列/页/索引图。正常新增、禁用、移除索引和删除均经原 HTTP 生命周期；首笔索引保存仅接受工作台返回的精确 index/child ID，并核对预期表、列、名称、真实会话创建/修改账号和项目，完成全字段基线登记；Capture 不认领其它同表索引，缺失、陌生新增或字段漂移即保留现场。禁用和移除前再次核对全部已登记记录，工作台 revision 防止检查后的旧快照覆盖；写后只接受本次明确启用/更新时间或墓碑变化。清理先逐边核验归属，再只移除已跟踪 ID，并复核两随机项目含墓碑零残留，失败则保留现场。测试不改平台既有配置图，不建业务物理表。新增索引图跟踪同时用于既有派生 API fixture，禁止仅凭随机项目自动接受未知根或跨表引用。

本切片只准备和编译实库测试源码，未运行实库或浏览器；无隔离配置时该用例明确跳过，不能记为通过。MCP 覆盖结论来自实际源码同 Service 调用链，本片没有执行新的 MCP 会话实测。本片已实际运行离线 `ConfigurationMaterializedValueTests`：新增 14 项及既有 78 项，共 92/92、零跳过、`-warnaserror` 零警告。API 测试源码 Release 编译零警告/零错误，准确发现该 1 项后因缺少私有数据库配置跳过（0 执行），不计实库通过。日志与 TRX 保存在私有 `.local/column-index-delete-results/`，不提交。
