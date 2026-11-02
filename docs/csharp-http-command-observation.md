# C#每请求私有SQL采集宿主（V44）

日期2026-11-02，起点8fe172f。独立工具程序集位于tools/performance/csharp-host，未修改平台Program、业务接口、生成模板、元数据或前端。只为后续同条件配对验收提供请求边界和私有索引，不代表已经完成真实SQL／数据库／性能验收。

## 显式接入与固定来源

使用ASP.NET Core原HostingStartup加载机制：宿主明确加载Metarill.Paired.SqlObservation程序集，并将Metarill:Paired:SqlObservation:Enabled配置为true才注册单例和IStartupFilter。未加载、未配置或false均不构造采集对象、不读取key／目录。原请求链执行一次，没有诊断端点、响应头、身份替换或新业务权限。

注册使用TryAddSingleton／TryAddEnumerable，宿主重放配置也只建立一层采集。原完整应用探针首轮确实复现双重注册：两请求计为四次、内层scope拒绝嵌套，索引正确失败。修复后保留原门槛，仍要求两请求／两记录，不把四改成通过。

默认构建引用仓库Data项目；固定独立包使用MetarillObservationDataAssembly MSBuild属性，直接编译引用该包已构建的同一Data.dll（不复制平台Data替换它）。运行时再读取实际加载Data程序集SHA256并核ExpectedDataSha256，不匹配则拒绝启动。工具不打开连接、执行SQL或猜测平台／业务库配置。

私有应用副本只追加采集程序集Reference及deps条目；原Program与生成业务保持。完整包的普通发布流程不自动启用本工具。加载方式、文件引用与输入摘要必须由后续固定协调器共同核验，不能只靠程序集同名判定来源。

## 私有设置

以下均为Metarill:Paired:SqlObservation下的服务端设置，HTTP请求不能开启或改变它们。

| 设置 | 要求 |
| --- | --- |
| Enabled | 仅显式true开启 |
| RunId | 32位小写十六进制本轮标识 |
| ExpectedDataSha256 | 实际包内Data.dll的64位小写SHA256 |
| ExpectedDatabases | 明确且不重复的逻辑库名，逗号分隔，最多16个；不表示物理服务器身份已核验 |
| KeyFile | 普通32字节私有key文件，不把值放命令行或日志 |
| OutputRoot | 既有普通私有根；.metarill-paired-observation-owner逐字等于RunId |
| MaximumRequests | 默认64，上限4096 |
| MaximumCommands | 每scope默认4096，上限65536 |
| MaximumConcurrentRequests | 默认4，上限64；超限不拦原请求，但整轮证据失败 |
| MaximumRetainedEntries | 默认100000，上限1000000；达到预算后不开始新采集，已有并发scope结束时可触发超预算失效 |

按V39—V41已接线路径读取Data观察器；同一AsyncLocal沿原异步链传播。SQL／参数对象／连接池／业务事务均仍由原Data路径负责，保留原结果和异常。scope关闭清零key副本；宿主持有有界的已关闭trace，到Dispose再复核、重写快照和摘要，发现此前迟到事件时不得保留正常索引。

X-Metarill-Sql-Sample只关联私有记录，必须是单一32位小写十六进制值；缺省也采集。重复／多个／路径文本标记均使证据无效，不进入文件名或业务授权。只保存异常类型、退出时状态和JDBC对应的Data快照，不保存请求正文、cookie、连接串或响应正文。测试使用公开合成key（32个字节11），不是部署秘密，不复用到正式验收。

每实例新建随机私有子目录，逐级拒绝链接／reparse和Windows网络路径；POSIX使用0700，Windows继承已配置私有根ACL。写入仅使用内部随机临时名并在同目录替换，采集IO异常不改原业务响应。输入key文件不会被修改或删除。

## 记录与完整性边界

记录格式metarill-paired-dotnet-http-v1，含runId／id／sample、statusAtMiddlewareExit、原链成功标志／异常类型、宿主问题及原生Data快照；枚举按camelCase字符串序列化。退出状态不是外层容器最终HTTP结果，真实客户端仍须校验状态、R和业务数据。scope内完整不等于命令或业务成功。

索引格式metarill-paired-dotnet-index-v1，含Data摘要、允许库名、开始／完成／已观察计数、条目预算、问题、记录摘要和finalized。必须在宿主释放后，固定源、全部预期样本、摘要／快照、计数和问题集合全部通过才表示内部完整。文件写失败、漏采、超预算、库名不符、嵌套失败或迟到事件都不能转成发布／性能通过。

只能证明已接线路径在当前逻辑链内的观察。仍须审计自行创建命令、抑制ExecutionContext、后台数据库工作及进程关闭后的覆盖；关闭前复核不能证明关闭后没有新任务。没有在Data层添加全局IO回调或用零SQL替代这些缺口。

## 本轮实际验证

- .NET10严格构建、23项真实Kestrel／HostingStartup测试通过，0失败／跳过；实际端口由操作系统分配。覆盖默认禁用、跨await、并发隔离、原R／异常、固定Data／库名、key／所有权、请求／总量／并发预算、运输歧义、IO失败、关闭前迟到事件和重复注册。SQL执行使用真实关闭的MySQL驱动，故意不Open，不把失败传输用例当真实数据库成功。
- 当前8fe172f冻结源码生成原同模型FullApplication；以包内Data二进制构建采集库，原Program、六个手写文件、393个前端文件及原元数据保持。plain-api／observed-api／probe-api三份Data SHA一致，Application.Host.deps包含显式采集库。
- 独立进程通过真实原Program和WebApplicationFactory启动Production完整包，未替换身份、Controller或数据库实现；修正后两条原明细路由各返回401，零应用SQL，关闭索引计数2／2完整。传输为TestServer，非原宿主真实Kestrel／MySQL；只证明匿名拒绝和装载，不计普通身份或业务SQL通过。
- 首轮完整包探针因重复HostingStartup计数失败，保留原资料；只修注册为幂等后同样原路由／计数门槛通过。新增IO测试初次枚举断言编译失败也保留；最终23项为唯一当前数量，不与中间15／22累计。

私有TRX及合成用例位于.local/csharp-http-observation-20261102；完整包／探针及验证汇总位于.local/csharp-observation-original-20261102/da604b08687f4a51b3dcf9e9605e1bf9。无Docker、建库、账号、远端写入或性能负载。CI新增23项跨系统验证及TRX上传，新SHA作业仍须按实际状态判断。

## 下一步

统一核验两端私有索引与实际SQL／参数／事务语义，补齐真实驱动时间等未知值路径，再冻结两端完整宿主和精确新范围，进入原Web／登录／业务／文件／Excel／回滚／重启实库采集。源头覆盖、资源前提、真实身份、浏览器／跨端及性能仍待；MySQL首版未整体完成，不复用已ended的V37授权。

2026-11-03 V46后续：[新采集镜像及原Linux Kestrel](paired-observed-images.md)已实际装载相同Data／采集二进制，禁网无库下两条原路由401及SIGTERM正常关闭、2／2私有记录经独立CLI通过，短容器新SSH核不存在；这补上真实Linux原宿主匿名接线子集，普通身份、实际业务SQL／事务与性能仍待。
