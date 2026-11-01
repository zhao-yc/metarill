# 全局缓存名称级联的R0真实SQL与Excel验收准备

2026-11-01，B118／B119。B118固定e1aebe1获本轮明确授权后已实际执行，0通过／6失败／0跳过；均在首轮真实XLSX的测试硬编码id表头处失败，另有非GUID临时目录清理拒绝。32表及本轮六临时目录已按授权清理并独立核零，失败保留。B119只修两处夹具，12项严格准备及六份保留真实工作簿解析复验通过；完整实库复验尚未执行。下方pending为执行前历史，最终状态见文末。

## 六模型范围

| 来源 | 全局缓存位置 | 来源键／父键 | 其它范围 |
| --- | --- | --- | --- |
| R0 | 根 | string／int64 | 其余普通项目目标 |
| R0 | 中 | int64／string | 其余普通项目目标 |
| R0 | 末 | string／string | 其余普通项目目标 |
| R0 | 根 | int64／int64 | 其它两级均明确全局 |
| R0 | 共享地区 | string／int64 | 地区→城市和地区→仓库分叉 |
| R0 | 共享地区 | int64／string | 主控件仅国家／地区，另一路保留仓库 |

共用B108/B109/B110实际元数据夹具，保留原开关、级联字段、页面、组件和中文表头。正式测试只给表及控件的实际物理表引用加本轮前缀；columnFullName的lookup.name表示外键角色路径，保持原样。

## 正式用例要求

使用既有Server-200／metarill_business_test与受限测试账号，不新增库、账号或权限。每模型仅五张、共享模型六张本轮自建表，共32张；全部表名在批准前由RunId和固定模型摘要确定。只有独立global-cached-cross-excel-mysql-v1用途、approved/running、Server/Database及完整无重复32表名单均匹配，才读取连接配置或连接数据库；旧A22／其它租约不能启用。

- 原生成Controller与Service通过importDatas30、initExport及实际XLSX下载完成全路径、根／中尾空路径、空值及导出后再导入；独立连接检查来源物理最深ID，R0不新增名称列。
- 缓存及普通目标的status=0、墓碑和异项目同名记录不能污染路径；来源其它项目数据不能进入导出。
- 来源只授import/export、目标list及来源普通写入拒绝；另明确给字典20页update用于真实生成改名传播。固定测试身份不是生产数据库Web/OAuth，不把该身份范围替代正式权限验收。
- 原生成字典更新必须实际刷新全局生产者lookup_name；R0导出读新缓存，旧文本导入失败，新文本导入正确，不能直接更新缓存后声称传播通过。
- 通知观察器在第二行提交前，用同一真实事务查询确认首行DML已执行后才注入失败；整组自有表完整摘要应保持，不能仅以预校验错误证明事务回滚。
- 共享冲突与同父缓存名称重名均拒绝，导出不得按排序猜选。实际导出目标查询须处于RepeatableRead且不新增FOR UPDATE，SQL日志不保存连接或参数秘密；不宣称并发／性能结果。
- 完成或失败后，仅按精确名单和原元数据核对结构、完整数据摘要及独立连接回读再DROP本轮自建表，随后独立连接核零；本轮XLSX与临时工程由原受限目录清理。业务首因与清理异常同时保留。

源码、宿主和生成器均是当前项目来源，没有接入模拟ADO。测试宿主为显式固定身份Development适配，不等于Production完整包／正式Web身份；真实MySQL、DML及XLSX也不能替代浏览器、跨端、文件引用／云对象、并发或性能。公开Full及原更新器继续拒绝未完成完整验收的Excel用途，不通过关闭导入导出、降低门禁或BackendOnly绕过。

## 已执行的准备与失败记录

最终Release、-warnaserror、--disable-build-servers及独占产物目录：12通过／0失败／0跳过，其中精确租约6项、真实六模型生成编译及静态ExportReadPlan30初始化6项。实际生成Import30Async／Export30Async存在，原手写partial重复生成保持，元数据输入不变，公开前端门槛继续拒绝。先前6项租约与最终批次重叠，不累计为18。

首次准备在六模型前缀JSON数组重挂时失败；重建独立节点后发现测试误改lookup.name，使原全局缓存证明正确拒绝。只修正夹具保留外键角色路径，产品Core/Data/生成器／权限均未改变；两轮原失败日志和TRX保留。原776份保护文件逐字保持。

证据位于.local/global-cached-cross-excel-mysql-20261101：build.log、build-final.log、lease.log、preparation.log、preparation-retry.log、preparation-path-fixed.log及results对应TRX。自动审核拒绝的首次实库命令没有测试进程／TRX，不记为六模型业务失败或通过。

下一步从提交独立固定源码和正式程序集、精确32表名单、只读实例及表不存在证明；经本轮明确批准后才执行及按范围清理。A22待答双库范围和已有其它冻结租约不变，不串用授权。

## 2026-11-01 固定来源与精确32表范围

e1aebe1537232bf459954db0584c03dab1ec2056独立固定2323源码／152正式输出；严格构建零警告错误，固定程序集12项复验通过（与前批重叠不累计）。实际正式程序集的纯ExpectedTables函数生成本轮六模型完整32表名单；不是按前缀授权任意表。

本轮RunId为53ec79891b51d3d9，六组均以mt_gcxe_53ec79891b51d3d9_及各自八位固定模型摘要命名；既有metarill_business_test／metarill_test，Server-200 MySQL8.4.11及实例pin只读核实，32张精确表全部不存在。完整名单见.local/global-cached-cross-excel-mysql-20261101/permission-plan.md，scope摘要0c81b5084d9fa4dfda267d70f42aa7bc1244dc38c7be3e10b878b31549bfa9cc，状态pending／not-started。

单次执行器4种失效租约零外部调用；实际六模型FQN分别在pending、ended与旧A22用途下共18次在连接配置读取前拒绝。三个负向批次不记为实库失败／通过；没有重试自动审核拒绝的原命令，没有生成本轮批准记录。

固定证据：source-snapshot.json、formal-assembly-files.json、fixed-build-state.json、fixed-preparation.trx、tables.json、pending-guards.json、pending-entry-0/1/2.trx及readonly-preflight.json。运行只会在本轮人类明确批准后开始；授权不串用于A22或其它范围，公开Full继续关闭。

## 2026-11-01 B118 实际失败与全部清理

用户call_3ead28a595f24555b5110978a6fb4cc0批准精确53ec79891b51d3d9范围后，固定e1aebe1单次真实执行六模型：0通过／6失败／0跳过，约1分30秒。失败均为测试Export解析器读取不存在的id表头；32表按每模型完整结构和数据摘要核对后DROP，原独立连接及新SSH再确认32表全不存在。原TRX、failure及execution不改写为通过，不重放已结束租约。

实际XLSX表头：普通三列“附件、名称、区域”，共享四列追加“仓库”，短控件原顺序为“区域、附件、名称、仓库”。元数据未将ID列加入导出，读取计划的隐式ID不等于用户导出列；不得为满足测试向产品新增ID表头。

本轮目录用了run-profile而既有TestDirectoryCleanup只允许GUID叶目录，守卫正确拒绝。按原已批准临时工程／XLSX清理范围，逐项验证原生Temp根、六个精确目录、504文件摘要、无链接与无活跃dotnet进程后用原生PowerShell清理；新读取确认六目录均不存在。六份真实工作簿复制到私有证据目录保留，未清其它任务或整个Temp根。原始execution.cleanupConfirmed只有数据库范围，后续独立本地清理回执补足，不覆盖原记录。

证据：原53ec79891b51d3d9/mysql.trx及test-result／execution／independent-absence；六模型evidence下intent／failure／cleanup与first-export-before-fixture-error.xlsx；workbook-header-audit.json、local-cleanup-before.json、local-cleanup-receipt.json、local-cleanup-independent.json、readonly-final-zero.json及formal-closure.json。原776保护文件保持，全部固定来源／输出复核通过。

## 2026-11-01 B119 两处夹具修正与复验准备

只修改测试：ReadOriginalWorkbook核对原三／四列表头和顺序，ID精度改在真实物理来源及各级键上检查，不增加导出列；本轮临时工程改用GUID叶目录，证据仍用固定run-profile标识。清理守卫未放宽。新增独立local-cleanup回执，下一轮执行器须同时证明32表和本地临时目录都清理；中段回执分别保留首轮导入／导出、原文本往返和真实末段DML回滚，后续失败不丢前段证据。

Release严格12项准备零失败／零跳过；另通过修正后的同一解析器复验保留的六份真实XLSX，逐项证明原表头、四条记录、全／根／中／空路径及其它项目未进入导出，6／6通过，无数据库操作。两批分别计数，未把保留工作簿复验计为新实库通过。

修正后真实原HTTP／SQL／传播／末段回滚完整场景仍未执行，公开Full门槛、原前端／模板／产品源码保持。下一次必须从新提交独立固定，并取得新的精确32表范围，不能使用B118已结束的旧表名或授权。

## 2026-11-01 B119 新来源固定与待批准范围

17d38cc1f3a90acb556287ad4d9dea83d504e592已推送并独立固定2323源码／152正式输出。严格构建零警告错误、固定12项准备复验通过；固定程序集的同一ReadOriginalWorkbook再对六份保留真实工作簿复验6／6通过，与前批重叠不累计。新执行器在连接前固定临时根，并要求每组local-cleanup回执、数据库与本地目录独立核零共同成立；不再将数据库核零等同本地文件清理完成。

新RunId为a57ce58d8a22b4b9，六组32表以mt_gcxe_a57ce58d8a22b4b9_及原模型摘要命名，完整名单由正式程序集输出。Server-200 MySQL8.4.11既定实例只读确认32表全部不存在。精确计划.local/global-cached-cross-excel-mysql-retry-20261101/permission-plan.md，scope摘要3385c419b2c0fb7af32a47e9ea1c31dea0825a86dfe2494def3a54bb26f4022f，pending／not-started，未创建新对象或运行实库复验。

外层4种失效租约零外部调用，六模型实际FQN在pending／ended／旧A22用途三批共18次连接前拒绝。源于B118旧范围的0／6失败、32表清理及六目录／504文件清理保持原记录；旧授权已结束，不覆盖新名字或修正源码。没有通过关闭原操作／增加ID列／放宽GUID守卫或公开Full门槛取得通过。

新固定证据：source-snapshot.json、formal-assembly-files.json、fixed-preparation.trx、fixed-workbook-replay.json、tables.json、pending-guards.json、pending-entry-0/1/2.trx及readonly-preflight.json。原776保护文件保持，当前产品／前端／模板无变化；完整SQL／名称传播／末段回滚的修正后实库结果仍待本轮明确批准。
