# double列表统计：MySQL静默溢出修复与只读实测

日期：2026-11-02。编号：A25。起点：`47588d2799edc141581970a8e94e4dc15ffad781`。本任务修正A24离线实现经真实MySQL发现的缺陷，不把A24的旧结果追记为本轮通过。页面、样式、交互、接口及原统计声明保持。

## 真实缺陷

在已核验的Server-200 MySQL 8.4.11上，用现有受限业务账号执行合成SELECT：两个double最大有限值的原生SUM，驱动返回System.Double类型的0。服务器侧CAST(SUM(v) AS CHAR)也为文本0；两条输入、MIN／MAX和参数文本正确，warning_count为0。不能把这个结果只归因于驱动类型转换，也不能假设服务器必然报1690。

A24的DoubleSumResult只拒绝非有限值，无法识别已经错误变成0的数据库合计。因此显式double聚合和finite检查不足以保证溢出安全，旧实现已被本轮替换。

## 源头修复

| 路径 | 当前行为 |
| --- | --- |
| 普通生成统计 | 固定物理double列调用FloatingStatistics，保留既有Query的项目、软删除、父范围及筛选；不改变已编译别名，不继承排序或分页 |
| 数据库聚合 | 按二进制指数分桶；桶内将按二次幂缩放后的整数系数以DECIMAL(65,0)求和，并按CHAR读取 |
| 有界返回 | 指数在-1074至971，共最多2046个非空桶，加null桶最多2047。LIMIT 2048位于GROUP BY之后，用于发现异常桶数，绝不截断参与合计的明细 |
| 应用合并 | 系数文本直接进入BigInteger，以2^-1074为统一单位累加，最后一次按最近偶数舍入为double；最终无法表示才拒绝 |
| 当前页合计 | 复用同一个整数累加器；合法抵消不因中间浮点溢出被拒绝，空页按原协议返回0 |
| 非冗余混合统计 | 每个double字段使用同一分桶定义；与原COUNT及精确类型SUM放在一条UNION ALL SQL中，共享语句读视图，不建立额外快照事务 |
| 精确类型及范围 | int／long／decimal仍走原decimal Sums，不经double中转。原固定字段、筛选绑定、项目、软删除、目标项目和跨表存储有效性检查保留 |

例如Max+Max最终溢出，明确失败；Max+Max-Max返回Max，Max-Max+最小非正规值保留该微小值。无值保留null，实际零保留0，再由原R.map.statistics的currentPage／total按原页脚显示。百分比原float计算和两位舍入口径不变。

分桶使用DECIMAL只承载最多约54位二进制的整数系数，不把原始double测量值转换成decimal金额。系数／指数／计数、重复桶、字段序号、全空桶及混合范围一致性均校验；非法结果不能变成0或部分成功。

本轮不承诺与旧逐项浮点累加逐位相同：double统计仍为浮点结果，但采用一次最终舍入，消除输入顺序造成的中间溢出和微小值丢失。整数、decimal及字符串ID合同不变。

## 实际验证与边界

| 验证层次 | 本轮最终结果及范围 |
| --- | --- |
| 数据自动回归 | 105通过、0失败、0跳过；整数累加的全指数、固定随机位型、最近偶数舍入、合法抵消、最终溢出，以及真实固定读计划／受控ADO的范围及坏桶拒绝 |
| 生成自动回归 | 31通过、0失败、0跳过；真实生成Controller、真实FreeSql连接前SQL翻译、普通与非冗余double、旧decimal／百分比、候选依赖双删拒绝及运行时升级保护；包含两种本轮FullApplication和两种A23相邻Full包的独立严格构建，不以引用仓库项目代替 |
| 真实MySQL只读合成 | 最终17项通过、0失败；其中16项为原生驱动、实际普通helper及非冗余reader的空／大／极小值、溢出、合法抵消和固定边界，另1项为全范围分桶往返。没有读取现有业务行 |
| 全范围分桶往返 | prepared二进制Double参数固定2231个输入，覆盖2098个二进制指数、128个固定随机有限位型及零、负零、null和正负Max；18条SELECT、2172个实际返回桶逐项核对独立IEEE整数期望，无精度放宽 |
| 正规生成保护 | 官方CLI严格Release零警告／零错误；平台及示例各两轮生成，215文件逐字稳定，24份手写partial、499份原管理端前端及9份元数据保持，共723份保护文件无变，无新增未归属文件 |

105、31、17分别记录，不与早期轮次相加，也不等于全后端或远端CI全部通过。当前正式目录没有double统计声明；两个新统计完整包及实际生成执行另由自动回归验证，不手改既有生成文件。

真实MySQL普通路径使用ORM受控AsTable合成源，非冗余路径先编译真实双表目录，再仅在私有探针替换FROM为合成SELECT。产品未开放这类任意SQL入口。这证明生产聚合SQL及驱动的固定读子集，不是持久化实表CRUD、真实Web身份、锁并发、Excel或浏览器验收。

每次连接首尾核对实例、版本、当前库和非root身份；所有命令限制为单条SELECT，未建库、建表或账号，未执行DDL／业务DML、会话写入或Docker操作，不产生需要DROP的测试对象。未消费B120等待批租约；其冻结文件再次只读核验匹配，仍pending／not-started、子进程0、数据库写入false。

## 输入精度诊断及原失败留证

全范围首轮用SQL文本构造输入，某个2^-958值的分桶系数比原C#位型期望少1，测试立即失败，没有放宽期望。独立SELECT随后证明：SQL字面量及未prepare参数在服务器读回的位型为040FFFFFFFFFFFFF，而原C#及prepared二进制参数为0410000000000000；两者显示相同的往返文本也不能据此视为相同位型。改用二进制传输固定输入后，保持原位型期望的全部2231项通过。

这个结果区分了输入解析与既存double值的聚合。它不证明现有写入、默认值或Excel参数均有逐位保证；A17／A23完整持久化链路仍须单列验证，不能用本轮合成输入代替。

其他失败也保留：最初只期待1690的探针假设错误；普通helper尝试修改query别名导致原WHERE引用失效，已修为保留原别名；生成回归首轮26通过／1失败仅因固定ORM输出limit 0,2048而断言只认LIMIT 2048，修正为仅接受这两种等价语法后最终31通过；一次私有探针变量重名严格编译失败，未连接。没有缩小数值、移除项目筛选或跳过原失败。

## 升级与回退

更新生成器和配套Core／Data运行时后正规再生成double统计Service及归属清单；必须同时包含新BinaryDoubleSum.cs和FloatingStatistics.cs，完整包候选同时删除源码及归属也会被精确拒绝。旧目标允许通过原CLI／在线助手明确同步升级，不能只替换生成Service。原手写partial与前端保持。

仅更换已有double统计实现无需DDL、数据回填或新增API；如实际物理类型未为double，仍须走元数据与原结构预览，不隐式改库。回退使用匹配的生成器、Core／Data及受管产物；回到A24旧原生SUM实现会重新引入静默零风险，不得作为已修复状态继续启用double统计。

分桶增加LOG2／POW、分组及混合字段分支的计算成本，尚未进行同条件负载测试，不能宣称性能不退化。真实持久表、全部复杂关系组合、权限与文件事务、原浏览器、跨端及性能继续验收，A24、复杂关系总项与MySQL首版不因此完成。

可复核命令（均在仓库根目录）：

```powershell
.tools/dotnet/dotnet.exe test backend/tests/Metarill.Data.Tests -c Release --nologo --no-restore --disable-build-servers --verbosity quiet -warnaserror --filter "FullyQualifiedName~BinaryDoubleSumTests|FullyQualifiedName~DoubleStatisticsTests|FullyQualifiedName~NonRedundantDoubleReadTests|FullyQualifiedName~NonRedundantReadTests|FullyQualifiedName~NonRedundantReadRequestTests|FullyQualifiedName~NonRedundantCrossTableReadTests"
.tools/dotnet/dotnet.exe test backend/tests/Metarill.Generation.Tests -c Release --nologo --no-restore --disable-build-servers --verbosity quiet -warnaserror --filter "FullyQualifiedName~DoubleStatisticsGenerationTests|FullyQualifiedName~OriginalListStatisticsTests|FullyQualifiedName~Statistics_upgrade_requires_missing_core_runtime_and_preserves_handwritten_service|FullyQualifiedName~Floating_statistics_candidate_requires_the_complete_bounded_runtime|FullyQualifiedName~NonRedundantDoubleGenerationTests"
.tools/dotnet/dotnet.exe build backend/src/Metarill.Cli -c Release --nologo --no-restore --disable-build-servers -warnaserror
```


## 后续A26：真实ORM参数筛选

上述输入位型差异随后已在生产ORM等值筛选复现；A26将浮点参数准备接至实际执行边界，真实合成读取、分桶统计筛选和原事务验证通过。这里的A25原失败与范围保持，不把A26证据倒记。当前参数传输、完整包依赖、成本及持久化／DDL未验边界见[后续专项](double-parameter-transport.md)。


## 2026-11-02：A27根因补充与可靠文本

A27在未联网的新.NET进程中进一步确认：当前R及默认JSON会将两相邻double输出成同一文本，回读已失真；此前文本／二进制差异的根因现定位到客户端格式化阶段，不能据旧观测归咎MySQL解析正确文本。当前已统一验证原文本、必要时回退G17；A25原实测及范围保留，见[根因补充与完整修复](double-text-roundtrip.md)。
