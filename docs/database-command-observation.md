# C#应用侧SQL与事务观察基础（V39）

日期2026-11-02，起点30c8cfa。承接V38的真实MySQL Performance Schema关闭前提，本项在应用执行边界采集C#证据，不修改共享MySQL配置，不以它替代Java／完整业务／性能验收。

## 默认行为与调用方式

DatabaseCommandObservation.Begin只供可信宿主显式开启，按当前ExecutionContext／AsyncLocal隔离。普通平台、生成应用和API没有新增开关、端点、DI必选项或默认日志；默认仅检查空观测上下文，原SQL／参数对象／连接池／事务／结果reader保持。活动scope禁止嵌套；父／子并发链、关闭后的迟到操作和在途关闭均有边界验证。

```csharp
// valueProofKey由私有验收协调器持有，不能放请求元数据、公开报告或客户端配置。
var trace = DatabaseCommandObservation.Begin(valueProofKey);
try { await ExecuteOwnSampleAsync(); }
finally { trace.Dispose(); }
var snapshot = trace.Snapshot();
// 还须核对来源、具体场景覆盖、预期命令及业务结果；内部一致性不是发布／性能批准。
```

内存证据有界，命令及事务分别默认4096条、最多65536；SQL最多32768字符、每命令256参数、文本值16384字符／字节值65536字节。超限／未知值／元数据异常不改变原业务，但添加问题并使内部一致性失败，不允许丢样本后报告成功。关闭时清零密钥副本及临时值摘要缓冲、释放驱动引用；观察不创建连接、文件或后台任务，不向外部回调传递可修改的DbCommand。

## 当前接线

| 范围 | 当前状态 |
| --- | --- |
| FreeSql及SqlRowStore／非冗余读取 | 已在原MySqlFloatingCommand六个同步／异步执行入口采集；准备与原Execute各一次，观察前后参数和原结果对象不换 |
| 身份、TIME回读、租约、关系传播／混合存储／维护及缓存核验 | 已将原生CreateCommand接回同一既有包装，不改SQL文本／参数表达式；具体业务实库覆盖仍待 |
| 事务 | DatabaseSession实际begin／commit／rollback API完成或失败边界，命令以同一CLR连接／事务引用令牌关联；未看到begin或关闭时无确认end会拒绝完整性 |
| 品牌差异 | MySQL准备标志与参数类型枚举只由MySQL适配器提供；通用scope／快照逻辑不判断品牌，不进入Service／Business |
| 尚未覆盖 | Metarill.Files五个直接DbCommand源文件、Schema直接命令、其它自行创建的事务／抑制ExecutionContext的任务、Java侧及完整HTTP场景采集；不能把这些缺口当作零SQL |

连接／事务数字是本scope的CLR引用令牌，不是MySQL线程ID或跨进程事务ID。事务条目记录实际API调用，不伪造服务器BEGIN／COMMIT文本；驱动内部握手／会话初始化和prepare网络往返未计入应用Execute条数。Prepared是真实驱动准备状态，不等于新发生一次网络prepare。

## 证据及值证明格式

记录实际CommandText、准备后绑定类型、名称／方向／大小／声明精度尺度、CLR类型、HMAC值证明、驱动是否进入、API返回／失败／取消及非查询影响行数。标量结果、reader数据、参数原值、连接串及异常正文不进入记录；原异常对象保持。reader返回只表示ExecuteReader已返回，后续读取错误／耗时不在本条目的成功或时长范围内，不能把该时间直接当数据库完整耗时。

valueProofFormat=metarill-sql-values-v1：HMAC-SHA256(本轮32字节私有key, UTF8(kind+NUL)+值字节)。integer为无区域性十进制，decimal为不含指数／多余尾零的精确文本，float／double为固定位型十六进制，日期／时钟保留种类与ticks，bytes按原字节；null与空串区分。值摘要用于规范值证明，不宣称等于协议原始字节；声明类型／精度尺度需另核，不能单凭摘要判SQL或业务等价。快照是可信生产器输出，配对工具仍须核原始JSON格式标记与来源摘要，不能把对象反序列化当输入验证。

InternallyConsistent只表示已接线路径在此scope内的计数／边界一致且无问题；既不保证所有SQL被观察，也不保证每条命令／业务成功，更不自动批准发布或性能。后续固定协调器必须同时验证全路径覆盖、各案例预期及原业务结果。

## 实际验证

- 最终数据层完整回归1555项：1528通过、27项外部环境跳过、0失败；其中新增观察／事务单元31项全部通过，包含六入口、实际ORM命令工厂、取消／原异常、未知值／预算／元数据失败、并发隔离／迟到／在途关闭、未知事务／缺end及原嵌套回滚语义。
- 最终自身完整源码／Production-Web装载2项通过、0失败／跳过；原32Service／32导入路由、六私有匹配计划、原共享前端及六原路由401保持。此处没有开启采集或连接业务库，不代替真实登录／覆盖写入。
- 独立显式只读1项实库通过：当前已有metarill_business_dev非root私有连接，固定Server-200实例，观察关闭／开启的实际返回值摘要相同；3条应用SELECT（同事务身份、7参数常量、FreeSql常量scalar）和2个真实事务API边界完整，原生与ORM double实际预处理、参数类型／值摘要匹配。只查询常量与本连接身份，不读业务表、不做DDL／DML／建账号／授权、不改GLOBAL或sql_mode；不是新独占库正式场景，未消费ended V37。
- 初次新增测试编译有三处误用：IAdo命令工厂需用现有AdoProvider公开入口、xUnit同步异常断言重载及AllowNull赋值。仅修测试后39／47／49项及中间1526／1527版本分别通过，均与最终1555项重叠，不相加。最终摘要改为实际reader返回值，decimal格式及异步关闭旧scope后显式开始新scope也有最终覆盖。

私有TRX、真实只读trace／result和计算回执位于.local/sql-command-observation-20261102；最终只读目录b496b3d9c8e44dc2ad116741ddf851c8，trace SHA995b587ec9475e97b70adf863cefb80eff71e4edffead8b8e1617bf298134bdd。源码、编译、只读与完整业务／负载分开记证据，后者仍未执行。

## 跟进与回退

无需DDL、回填、原页面或表级再生成。生成项目需使用匹配的新数据运行时源码重新打包／构建，手写partial继续保护；V37的63f60dc／原三镜像只证明其冻结来源，不自动升级为本实现的新运行时验收。已有observer默认不启用；回退配套Data源码／包，不修改业务数据。

下一步先完成文件模块原生命令和其它直接路径的统一工厂，接真实HTTP固定案例与Java对应采集，再冻结双方业务数据／SQL／参数／次数／事务／资源证明并执行同条件负载。V08、跨端与MySQL首版总验收继续未完成，不改用微基准或只比较容易通过的SQL子集。
