# 浮点参数的真实MySQL传输与等值筛选

日期：2026-11-02。编号：A26。起点：`1d5377b8de6879834dbce415d73cbf9a51876441`。沿A25已发现的SQL文本输入差异继续追踪生产路径，仅修改数据库基础设施及完整包依赖检查，保留原前端、接口、权限和元数据。

## 真实红灯

生产MySqlDatabaseProvider创建的FreeSql执行合成源SELECT：来源POW(2,-958)读回位型0410000000000000，与请求double完全相同；原参数化等值筛选却返回0条。A25已独立定位其文本解析会落到040FFFFFFFFFFFFF，本轮证明该问题确实影响生产ORM筛选，不能只把它当成探针字面量问题。

尝试在CommandBefore中调用Prepare也不成立：真实事件触发时Command.Connection可能为null，探针实际抛出Connection property must be non-null，未完成第二分支。不能临时抢开连接、绕开原事务或手动帮助测试查询后宣称运行时已修复。

## 源头实现

MySqlFloatingCommand只包装真实MySqlConnector命令，在参数已绑定、连接和事务已就绪的实际Execute入口检查浮点参数。含有限double／float且尚未准备时，在同一命令调用Prepare／PrepareAsync，再执行原命令；非有限参数明确拒绝。三个同步和三个异步执行入口一致，取消沿原令牌传播。

没有改写SQL、占位符、参数对象、物理列或类型，也没有将值变成decimal／字符串。无实际浮点值的命令继续原文本路径。若IgnorePrepare使驱动准备成为空操作，执行前明确拒绝，不能静默退回已知会漂移的传输。已准备命令继续沿驱动现有机制复用。

生产ORM通过FreeSqlBuilder的提供程序扩展入口安装公开AdoProvider适配；原ADO继续负责方言转义、参数生成及原池归还。复用同一个主池、标识和原真实连接／事务，不重建池。提供程序生命周期同时收尾两份ADO状态；命令异步释放也完成包装组件与原驱动的Disposed合同。本接入仅覆盖当前单主库配置，未交付读写分离。

SqlRowStore的事务／自有连接命令，以及MySqlNonRedundantReader的共同Command工厂使用同一包装。业务Service和前端不出现品牌判断。原TIME写后回读、文件／权限事务、范围编译、整数ID、decimal及二进制类型规则不变。

适配以本地固定FreeSql 3.5.311及MySqlConnector 2.6.1的实际编译和执行为准。官方源码说明了文本浮点格式与二进制参数写入的区别，以及ADO命令工厂／连接池合同：[MySqlParameter](https://github.com/mysql-net/MySqlConnector/blob/2.6.1/src/MySqlConnector/MySqlParameter.cs)、[FreeSql公开ADO基类](https://github.com/dotnetcore/FreeSql/blob/master/FreeSql/Internal/CommonProvider/AdoProvider/AdoProvider.cs)。只使用其可访问扩展点，不反射修改依赖私有字段，不复制或维护一套ORM源码。

## 验证范围

新增13项离线命令回归通过、零失败零跳过：实际驱动命令的属性／参数／连接透传、六入口非有限拒绝、三个异步取消入口、原池复用及转义／参数等价、实际查询／插入／更新SQL逐字一致、禁止自动结构同步和标准异步释放。SQL比较不执行任何写入。

最终真实MySQL只读用例1通过、0失败、0跳过，使用当前生产提供程序且不在测试钩子中手动Prepare：

- 14个代表性double的实际参数返回按IEEE位型精确比较，含原故障值、正负Max、正负最小非正规值、正规／非正规边界、相邻1、大值、极小负值和零。
- 实际ORM等值筛选正确1条，相邻值严格0条；原分桶统计在double筛选下得到正确合计。
- 原真实事务下ORM读取及包装的六种执行入口通过，保持同一个连接和事务，最后显式回滚；两个write执行方法执行的仍是固定SELECT，不是DML测试。
- 含浮点的混合命令保留超过JS安全整数的decimal、中文／引号／反斜杠、二进制字节、datetime(6)、扩展TIME及null；纯decimal命令确认未被准备。
- IgnorePrepare=true的独立只读连接明确拒绝浮点命令；实例／库／受限非root身份首尾核验。

该测试使用独立的METARILL_FLOAT_READONLY_*开关、实例pin和SELECT_ONLY确认，默认跳过，不复用允许建表／清理的实库开关。业务查询全部为固定单条合成SELECT，另有系统变量核验及驱动开启／回滚事务；未读取现有业务行，未创建库／表／账号／会话，未执行DDL／业务DML或Docker操作。环境开关在本轮进程收尾清除。

## 原失败保留

两次早期私有探针因遗漏using和空值静态检查而严格编译失败，均未连接。直接使用不可访问的MySqlAdo派生方案也被产品编译器拒绝，最终改用公开基类委托原方言，没有放宽访问控制或反射私有类型。

只读混合参数首轮失败于把未声明结果类型的原始参数当byte[]读取；合成SQL改为明确CAST(@bytes AS BINARY)后，继续要求原始三个字节逐一相等，其余数值／时间断言不变。原失败用例及之后成功结果分别保留。新增标准释放红灯0通过／1失败定位到包装组件未触发Disposed，修复后13项全部通过；不把早期成功批次转记为最终来源通过。

## 升级、成本与剩余门槛

更新Core／Data运行时与生成器后，既有应用通过原CLI／在线助手成套同步；新MySqlFloatingCommand.cs是当前完整候选的必要启动归属，同时删除源码与清单也会被拒绝。旧目标允许显式同步升级，手写partial不覆盖。此处无新增页面、路由、客户端参数、DDL或数据回填，既有不含浮点的生成产物保持。

回退必须配套恢复Data工厂及共同命令入口。回到旧文本浮点传输会恢复已实测的等值筛选风险，不能将回退状态标为问题已修复。

预处理可能增加首次往返及服务器准备资源，尚无同条件负载／并发结论，不能宣称性能不退化。当前证明固定只读传输与筛选子集；实际持久化CRUD、导入／默认值、完整复杂关系与文件回滚、浏览器／跨端／TLS和性能继续验收。SQL数值字面量及DDL默认值仍走原路径，本次未证明其实际创建结果逐位等同，不把A17／A23或MySQL首版整体标为完成。

## 最终回归与生成保护

| 验证 | 实际结果 |
| --- | --- |
| 数据整组 | 1488通过、0失败、25项实库条件跳过；本次独立只读用例在无开关整组中属于跳过项，另以明确开关实际执行1／1通过 |
| 平台API整组 | 223通过、0失败、49项实库条件跳过；严格构建当前平台宿主，保留原路由／权限与基础设施合同 |
| 生成及完整包组合 | 32通过、0失败、0跳过；包含实际double Controller／SQL翻译、普通及非冗余Full、相邻A23两个Full的独立严格构建、旧运行时更新，以及新必要源双删拒绝 |
| CLI及原产物 | 官方CLI严格Release零警告／零错误；平台／示例各两轮生成215文件逐字稳定，24份手写partial、499份原管理端前端与9份元数据保持，共723保护文件无变化 |
| 待批冻结范围 | B120仅check-only，冻结核对匹配，仍pending／not-started、子进程0、数据库写入false；不消费旧B119授权 |

13项命令回归已包含在1488数据整组中；新旧批次不相加，也不把条件跳过记为通过。原32项生成／223项API成功后因补齐标准Disposed合同重新执行了上述最终组合，早期结果保留。远端CI须按后续提交SHA重新核验，不把本地结果记为CI全部成功。

核心离线命令：

```powershell
.tools/dotnet/dotnet.exe test backend/tests/Metarill.Data.Tests -c Release --nologo --no-restore --disable-build-servers --verbosity quiet -warnaserror
.tools/dotnet/dotnet.exe test backend/tests/Metarill.Api.Tests -c Release --nologo --no-restore --disable-build-servers --verbosity quiet -warnaserror
.tools/dotnet/dotnet.exe test backend/tests/Metarill.Generation.Tests -c Release --nologo --no-restore --disable-build-servers --verbosity quiet -warnaserror --filter "FullyQualifiedName~DoubleStatisticsGenerationTests|FullyQualifiedName~OriginalListStatisticsTests|FullyQualifiedName~Statistics_upgrade_requires_missing_core_runtime_and_preserves_handwritten_service|FullyQualifiedName~Floating_statistics_candidate_requires_the_complete_bounded_runtime|FullyQualifiedName~NonRedundantDoubleGenerationTests"
.tools/dotnet/dotnet.exe build backend/src/Metarill.Cli -c Release --nologo --no-restore --disable-build-servers -warnaserror
```


## 2026-11-02：A27根因补充与可靠文本

A27独立在数据库连接前复现R／默认JSON的相邻值文本碰撞，补齐根因与统一格式化修复。A26二进制参数接线仍保留：驱动自身文本格式化不经过平台辅助方法。原A26实测及边界保持，当前JSON、默认值与规则合同见[后续A27专项](double-text-roundtrip.md)。
