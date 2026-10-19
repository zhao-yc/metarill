# 关系导出授权与父范围

原 `initExport`、投影列、筛选和文件下载 URL 继续沿用。此切片修复 C# 关系名称导出先前直接查询目标实体、未验证目标页面读取权限以及把来源父直接用于独立目标父的问题，不重写原列表或详情界面。

## 固定授权

含已编译 List 名称的导出，Controller 在进入导出准备和配额之前验证固定目标页 `list` 权限。生成 Service 同时注入真实 `IRuntimeAuthorization`，要求传入 Actor 与当前真实 Actor 相等，并重验来源 `export` 和目标 `list`；直接 Service 调用不能省略目标授权。宿主未注册真实授权或手工构造 Service 漏注入时明确拒绝，不使用默认放行实例。生成 DI 与现有 Multi 的授权依赖保持同一注册方式。

目标页、名称列及项目/软删/页面/控件范围全部来自稳定元数据，不能由请求选择。目标名称列仍必须在目标页已开放投影内。目标查询只读取 `id/name`，不会因导出名称读取目标整实体。

## 每行真实父上下文

来源真实父检查抽为 `ValidateOptionsSourceParents`，由候选和导出共用；即使导出结果为空，也不能跳过来源父存在、项目和软删检查。

目标父映射复用控件的同一元数据证明：同父定义使用页面既有 `parentId`；独立目标父只使用当前页唯一且可读、声明关联到该父表的外键。该依赖即使不在列表导出列中，也仅作为内部固定读取，不增加 Excel 表头或 VO 字段。隐藏或多义依赖仍可正常生成页面，真正需要名称导出时明确拒绝，不退回更宽的目标页，也不要求客户端补任意父值。

源行按真实目标父分组，每组复用候选 `OptionsScope` 验证两个真实父及完整目标范围，再参数化读取目标 ID 集合。名称缓存键是 `(目标父, ID)`，相同 ID 不会借用另一个父组的名称。目标缺失、已删、外项目或当前范围不可读时保留来源 ID 文本，沿用已有导出的回退表示，不输出范围外名称；真实父自身非法则整次导出失败。

只有普通 List 中已编译的目标名称属于本切片，未将未实现的选择控件或动态表达式标为支持。独立父很多时会按父分组读取，仍受既有 `MaxRows` 总量限制，本切片不作性能提升声明；后续优化应保持相同范围和投影。

## 验证

新增实际生成编译、真实 HTTP/Service 与 Server-200 随机三表测试：独立父与同父、两个父组、同 ID 跨父不串名、外项目/软删/不存在目标不泄露名称、来源及目标无权限在准备前拒绝、空结果非法来源父仍拒绝、非法目标父整次不发行、隐藏/多义父映射明确拒绝、真实 Actor 不一致及漏授权依赖拒绝。AOP 检查目标读取不包含未开放私密列，也没有 `SELECT *`。投影接收器用于直接检查真实 Service 的有界结果，不把它冒充 Excel 文件；文件格式和无头下载由既有实际独立包/平台 HTTP 回归覆盖。测试结束仅删除本轮随机表并反查不存在。

2026-10-20 冻结验证：本切片初次定向 3/3 通过，扩大回归前曾被原详情页级 `nameField=id` 覆盖丢失目标页阻断，已保留失败记录并集成独立修复，未改合法夹具。完整 Generation 172 项结果为 164 通过、6 失败、2 项 FreshInstall 专属配置跳过；失败中本fixture在安装阶段与程序集并发DDL争用同库唯一结构锁，尚未执行导出。随后只给此fixture加入不与其他collection并发的测试调度，未给产品加重试或放宽结构锁；导出/选项/Multi/独立包HTTP与MySQL的准确非空定向 6/6 通过、零跳过。其余完整回归失败为四项旧精确错误文本断言及独立前端验收脚本调用旧简化 `save()` 方法，已分别交原UI/脚本维护切片处理，完整套件仍需整合后复验。私有证据在 `.local/relation-export/relation-export-generation-full-v1.trx` 和 `relation-export-target-v4.trx`。不将这次定向通过写成完整套件全绿。

计数澄清：上述最终 6/6 是 `GeneratedRelationExportMySqlTests`、`GeneratedContractTests`、`GeneratedRelationOptionsMySqlTests`、`StandaloneExportMySqlTests`、`GeneratedMultiSelectMySqlTests` 及 `ApplicationPackagerTests.Standalone_package_builds_from_its_own_sources...`；最后一项调用 `StandaloneExportHttpProbe`。不包含 `StandaloneFilesMySqlTests`，该类包内前端 HTTP 脚本在完整回归中的旧 `save()` 失败仍待其独立修复。
