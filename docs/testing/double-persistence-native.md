# double正式持久化验收记录（A30）

日期：2026-11-02。产品起点：`f5ed1551a4837285401f95550c53595644eca087`。本项为[MySQL首版节点](../mysql-first-release-milestone.md)补齐A17／A23—A29尚缺的真实持久表证据。**A30固定57cc81f已正式执行，反查阶段因夹具decimal(30,9)超过C# decimal整列可表示范围而失败；资源已清理、租约已结束。当前源码修正夹具为decimal(28,9)，数值默认／CRUD／统计／Excel／事务全链仍待新批次验收，不能记为持久化通过。**

## 同一份元数据与实际完整包

复用已有非冗余三表模型，实际生成普通party、非冗余order和group，保留项目54／后台5、原List关联、派生customer.amount和真实group_id=@parent_id父范围。元数据统一声明double，新增四个物理浮点默认出口及一项精确decimal对照；不手写Entity／Mapper／Service或业务DDL，不修改原页面／模板／接口。

| 表／字段 | 类型 | 默认值位型 |
| --- | --- | --- |
| mt_double_native_order.measurement | double | 0410000000000000 |
| mt_double_native_order.measurement_previous | double | 040FFFFFFFFFFFFF |
| mt_double_native_party.amount | double | 0410000000000000 |
| mt_double_native_party.previous_amount | double | 040FFFFFFFFFFFFF |
| order.customer_amount | 派生double，来自party.amount | 不进入物理列 |
| party.exact_amount | 当前夹具decimal(28,9)，原A30冻结夹具为decimal(30,9) | 对照值9007199254740993.123456789共25位有效数字；原超范围反查拒绝保持 |

通过公开ApplicationPackager创建真正FullApplication／web-database-v1包，不使用BackendOnly或改造Program。普通与非冗余页均声明原导入／导出及double统计；两份手写partial只用于观察真实首行DML后拒绝第二行，正规再生成两轮保护它们。实际包已独立严格构建，当前Core／Data／Exports关键数值源码一致，原运行时JS字节保持。

## 已编译的正式入口

`Metarill.Generation.Tests.DoublePersistenceNativeMySqlTests.Production_web_double_defaults_crud_filters_statistics_excel_and_rollback_keep_binary_values`。

驱动按以下顺序实现；原A30只执行到第2步后的默认值／反查阶段，后续步骤未通过，详见末节：

1. 专属用途／批准／运行中租约、产品基线、固定准备根、三表名单及512条样本预算先校验；原SheetJS依赖缺失同样在复制、宿主和连接之前拒绝。旧B121租约不能启用本用途。
2. 实际Production宿主禁用DevelopmentBootstrap，普通启动／预览保持空库，显式结构安装及重复零差异；预期本模型3业务表＋11基础表。结构预览、默认反查草案与往返通过原SchemaService／规划器。
3. 通过缺列SqlRowStore INSERT读取数据库实际默认值，逐位核对四个默认；不以C#实体默认值代替COLUMN_DEFAULT。真实管理员初始化密码保持、原三字段普通账号登录，菜单与页动作分开授权。
4. 普通生成CRUD验证默认、相邻值更新、省略与null；真实typed仓储矩阵含正负极值、最小值、已知相邻值、64个固定随机值及boxed float；ID和decimal对照保持。原分页、数值等值筛选及非冗余派生／父范围、复制／软删通过实际生成接口。
5. 普通及非冗余统计分别验证Max合法抵消后为1、两个最小值相加、Max+Max最终溢出拒绝；JSON非有限／溢出／非零下溢拒绝及无业务写入。
6. 普通和非冗余导入第二行手写规则必须实际读到第一行DML及原位型、当前事务后才故障，整批三表摘要保持。快照使用当前可靠JSON，不能因旧格式本身合并相邻值而掩盖变化。
7. 真实下载XLSX、数值类型／相邻位型及派生值回读，执行原SheetJS选择方法。只把原回调中已开放的数值列送入生成导入器，派生列原只读门禁保持；不声称所有导出只读列都能原样提交。
8. 真实普通账号导入／导出撤权后无写入，同地址重启保持会话、注销拒绝与结构零差异；固定包不启动，实际执行在独占副本中。停自有宿主后清理本轮storage/GUID，数据库及账号由未来精确外层租约按所有权清理并独立核零。

原数值HTTP请求用JsonDefaults.Options编码，防止测试客户端的默认double格式先丢位；分页Total按原StringLong转换器的字符串Token核对。没有为测试更改产品协议、权限或统计语义。

## 正式执行前的历史离线验证

| 层次 | 已执行结果 |
| --- | --- |
| 元数据与完整包 | 2通过、0失败、0跳过：四默认位型、实际非冗余读计划、普通／派生统计、公开Full独立严格构建、两份partial两轮保护、关键运行时及原JS保持 |
| 汇总离线准备／门禁 | 16通过、0失败、1正式用例条件跳过；包含上述2项及14项专属范围负向合同，不累计为18项 |
| 最后驱动编译／门禁 | 14通过、0失败、1正式用例条件跳过；在读取实际分页合同后校准Total为字符串，并严格编译驱动，与前组重叠 |
| 实际已编译入口拒绝 | pending、ended、旧B121用途3项均按精确租约错误拒绝；无数据库配置，未创建执行目录 |
| 原文件保护 | 723个已跟踪生成／partial／前端／元数据文件摘要保持；本轮未修改产品、页面或元数据源文件 |

匿名6个原page／importDatas／initExport入口在真正生产宿主返回401，没有通过测试Actor进入业务。完整包源码、文件集合、身份模式、元数据及手写规则漂移均拒绝；重新捕获不能收编额外源码。执行副本仅原宿主结构日志可增加，不能污染固定原包。

首轮新增测试误写HttpMethod命名空间而未编译；正式驱动初次编译缺PasswordHasher引用及排序方法组无法推断，均只修正测试，原日志保留。随后读取真实StringLong合同纠正Total读取假设，此项发生在新库执行之前，不是实库失败后放宽断言。显式命令超时也先保存脱敏首因，不自动重放。

证据保留在本机忽略目录`.local/double-persistence-native-20261102`，包含TRX、实际入口拒绝和verification.json。正式用例的条件跳过仍是未执行，不以公开包严格构建、匿名HTTP或驱动编译替代实际默认／CRUD／事务验收。

## 正式执行前的历史准备

独立冻结来源／程序集／Full包及Node／原SheetJS依赖已完成，见下方固定批次；外层单次协调器、精确新库／账号／存储清单和只读实例／对象预检现已完成，见下方准备结果；尚须本轮直接授权。当前没有新建库／账号或执行DDL／业务DML，也没有正式测试结果；不借用已结束B121租约。

后续正式完成后也只关闭该固定数值三表子集。全部复杂关系、浏览器、跨端、并发及同条件性能仍按进度表验收，不因此宣布MySQL首版完成。
## 独立固定批次完成（57cc81f）

从57cc81fad166ef5d201a6c12ec29e1e2f8d75d7f建立独立普通克隆，2384份跟踪源码／152份正式输出／947份实际Full包文件已逐项固定；严格Release零警告／错误，新固定程序集的16项准备／门禁全部通过、0失败／0跳过。该批仅运行2个准备和14个租约测试，没有运行正式新库用例，与前述批次不累加。

按原package-lock以ignore-scripts安装27项Node依赖，没有改动锁文件；Node v24.17.0真实可执行文件、xlsx.mjs、锁文件、原选择方法JS及原Excel探针文件均保留独立摘要供后续外层协调器绑定。不能只固定C#包而让实际原解析器漂移。

首次离线汇总包装器误沿用B121的“不得存在公开Full标记”检查，在16项已通过及真实Full包已建立后汇总断言失败。A30本来就要求真正Full，因此校正包装器为必须存在.metarill-package.json，再纯文件核验FullApplication／web-database-v1／专属namespace、全部源码／程序集／包文件与16项TRX并完成固定；没有修改包、重新构建或重跑准备来掩盖失败。原失败说明与日志保留，该次不是数据库或业务失败。

固定副本保持只读，后续业务仅在独占执行副本运行。外层协调器及可审查精确资源计划现已完成，详情如下。未创建新库／账号或执行DDL／业务DML，未消费新租约；A30保持待正式验收，不能标记完成。
## 单次协调器与正式入口护栏完成

在不修改上述固定源码、程序集或完整包的情况下，外层协调器现已绑定全部来源、输出、包、SDK、原Server-200租约脚本和实际Node／SheetJS摘要；纯文件check-only通过。八项未批准／已用／审计缺失或不匹配／参数错误均拒绝且子进程、随机秘密和所有权写入为零；固定正式程序集的pending、ended、旧B121用途三个实际入口也在连接配置前明确拒绝，没有创建执行工程。该批与先前主仓三个入口检查不累计。

只读SSH预检已核对Server-200及MySQL固定实例，本轮唯一库、账号及所有权文件均不存在。只执行系统变量、对象计数与文件存在检查，没有创建资源或DDL／业务DML。

本轮运行17bba128f50252b9，只计划新建metarill_fresh_17bba128f50252b9_agent和mtf_17bba128f50252b9@%，真正Full宿主显式安装三业务表加十一基础表；三业务表各最多512条合成记录，不额外增加CHECK。限15分钟，一次执行；固定副本不启动，仅独占执行副本运行。精确物理表、权限和storage/GUID清单见本机忽略目录double-persistence-native-20261102/permission-plan.md及lease-intent.json，范围摘要b23b4391da49a665d002f08ef13bfdb471a73f07fbaf8fe5644818d13c6f7b8a。

停止自有宿主后，仅清理本轮storage/GUID合成uploads／exports；远端按实例与所有权核对后删除同轮库／账号，用新连接及独立新SSH核零。工程、原始失败和核验材料保留。清理未确认时备份本轮所有权恢复材料，不自动重建／重跑。该段记录执行前pending准备；本轮随后已授权并单次执行，现已失败、清理及结束，见末节。

## 2026-11-02 单次正式失败、清理与修正

用户直接授权后，原固定57cc81f／运行17bba128f50252b9单次执行，正式结果0通过、1失败、0跳过。失败阶段为“真实默认值及反查”：mt_double_native_party.exact_amount的decimal(30,9)触发COLUMN_UNREPRESENTABLE，提示C# decimal不能无损覆盖超过28位精度的全部数值范围。原反查器拒绝正确，未放宽产品规则；缺列INSERT的DB默认、真实登录、CRUD、统计、XLSX及事务回滚等后续流程未通过，不能凭前置建表或旧离线结果记为完成。

执行器及新SSH独立核验确认本轮唯一库、账号不存在，合成storage已清理，private-settings／private-ownership已删除；execution回执cleanupConfirmed=true、leaseStatus=ended、autoRetry=false。原固定来源、完整包、失败TRX及错误证据保留，不重用本轮一次性授权。

当前修正仅调整测试元数据precision=28、scale=9，对照值9007199254740993.123456789保持。新增整份三表模式正反双向零差异回归，以及precision=30仍按原规则拒绝且输入不变的负向回归；新增正向用例先实际复现同一错误，再修正夹具。完整包回归中出现源码CRLF与原样字符串LF不一致，按仓库.gitattributes恢复源码LF，未改校验、钩子内容或包摘要要求。暂停中止的运行不计通过；恢复后严格Release回归18通过、0失败、0跳过，包含实际公开Full独立严格构建、六个原匿名入口401、四项准备及14项租约门禁；各批重叠不累加。723个跟踪生成／手写partial／前端／元数据文件原始字节保持。

新正式复验必须从修正提交重新固定源码、程序集、真实Full及Node依赖，并建立新精确租约；原57cc81f包和已结束17bba128f50252b9授权不能替代。没有修改产品、前端、生成模板、业务断言或数据库范围拒绝规则。

## A31 修正后新固定批次准备完成

从03177fb7b7bf3e4487eec1da6108a84d1b0688de独立普通克隆重新构建，2396份来源、152份正式输出、947份真正Full包文件及原Node v24.17.0／SheetJS／选择方法均固定。产品起点仍为f5ed155，实际运行时全部绑定本次03177fb来源，不复用旧57cc81f包。新固定程序集严格构建零警告／错误，18项准备／租约全部通过、0失败／0跳过；与主仓18项重叠，不累加。

单次协调器纯文件check-only通过，8项外层未批准／已用／审计缺失或错配／错误参数均零副作用拒绝；3项已编译正式入口pending／ended／旧用途均在连接配置前拒绝。新SSH只读核实Server-200及固定MySQL实例，新库、账号与所有权文件不存在。当前未创建资源或执行DDL／业务DML，未生成正式口令或私有settings。

新运行5fa7c6430b9c5588仅提议唯一库metarill_fresh_5fa7c6430b9c5588_agent、账号mtf_5fa7c6430b9c5588@%，3业务表加11基础表，逐业务表512条合成记录上限、900秒一次执行、不新增CHECK。数据模型仅将对照decimal改为28／9，原真实默认值、CRUD、位型、统计、Excel、首行DML后回滚、撤权和重启断言保持。全部精确表、权限和storage/GUID及不可逆清理范围见.local/double-persistence-range-native-20261102/permission-plan.md；scopeSha256为2c4c50e67d579d12d0c27ea4a882c383b3b60b2384b5f7e01be3347554f945d0。

该段记录A31执行前准备。用户随后直接授权，单次执行现已失败并清理结束，详见下一节；旧授权不再用于新批次。

## A31 正式执行：默认值与反查通过，生成响应失败

用户直接授权call_4cb06390e0e3485db1818270815ad0cc后，5fa7c6430b9c5588按原固定03177fb执行一次，正式0通过、1失败、0跳过。通过了Production空库启动不建表、显式14表安装／重复零差异、整表反查双向往返、缺列INSERT四物理double默认逐位核对，以及管理员重复初始化密码保持、原三字段普通账号登录和三菜单归属／非super检查。

失败位于普通及非冗余数值闭环的第一笔普通add回执：amount预期位型0410000000000000，实际040FFFFFFFFFFFFF。没有将“接近相等”改作通过；其后的完整typed矩阵、派生CRUD／筛选、统计、Excel、回滚、撤权和重启均未通过。

新SSH与新连接独立确认唯一库metarill_fresh_5fa7c6430b9c5588_agent、账号mtf_5fa7c6430b9c5588@%不存在，自有宿主停止及storage合成对象清理确认，private-settings／private-ownership删除；execution为passed=false、cleanupConfirmed=true、leaseStatus=ended、privateSettingsDeleted=true、autoRetry=false。原失败TRX／固定包及证据保留，不重用本轮租约。

后续定位并修复独立宿主模板的double JSON登记缺口，见[A32源头修正与HTTP回归](standalone-double-json.md)。已有平台主服务登记正确；本轮未据回执错误宣称数据库写入本身失真。修复提交后的整个数值链仍需新的固定批次正式复验。

## A33 宿主修正后完整数值链复验准备完成

恢复时已核对8fdf784与远端一致、工作区干净；中断前A33只有准备脚本，没有遗留构建进程、固定克隆或远端租约，未将旧句柄失效当作已经执行。随后从8fdf784df803fc5a340e7a0afacc49967ef379df建立新的独立普通克隆并严格构建，2398份跟踪来源／152份正式输出／947份真正FullApplication文件固定；原Node v24.17.0、SheetJS、锁文件、原选择方法及Excel探针摘要同时绑定。公开包Program实际包含MVC与最小接口的两处有限浮点登记，不能借准备测试通过却继续使用旧宿主。

新固定程序集的20项准备回归全部通过、0失败、0跳过，含A31原四项模型／Full准备、14项租约负向合同及A32两个宿主真实HTTP片段精度回归；不是20项实库用例。Full自身严格构建零警告／错误，原匿名入口拒绝、元数据／受管来源／手写钩子和重复生成保护保持。主仓此前24项与更新保护3项另列，批次重叠不累加；723份原前端／元数据／生成及手写保护文件仍原始字节不变。

单次协调器8项未授权／已用／缺失或错配审计／未知参数护栏均拒绝且副作用0，固定正式入口pending／ended／旧用途3项均在连接前拒绝。全文件check-only保持frozenVerified=true；新SSH只读核对Server-200和固定实例，新库／账号／所有权文件均不存在，没有创建资源或执行DDL／业务DML。

本轮新运行207e26147134d5ff，唯一库metarill_fresh_207e26147134d5ff_agent、临时账号mtf_207e26147134d5ff@%；三业务表加十一基础表，每业务表最多512条合成记录，限900秒一次执行、不新增CHECK。完整重验实际DB默认／反查、原普通身份及导航、普通和非冗余CRUD／派生筛选、位型矩阵、精确ID及decimal对照、统计抵消／溢出、原XLSX与选择解析、真实首行DML后整批回滚、撤权及同地址重启／注销。原位型和业务断言保持，不缩减为仅复验第一笔新增。

精确表／权限、独占storage/GUID合成文件与不可逆清理清单见.local/double-persistence-json-native-20261102/permission-plan.md。范围摘要d85f0314aebf6ce48baa1e02f1b93f2cebfd4fb2fe747ad6841677ddaf28da98。此段记录正式前pending／not-started准备；随后用户直接授权，A33已单次执行、失败清理并结束，详见末节；原A30／A31及本轮授权均不覆盖下一组新对象。失败不自动重试，清理后按新连接和独立新SSH核零；原冻结资料、工程及失败证据保留。

本项正式结果尚无；A32修复不能代替数值全链通过，CI仍以对应SHA实际结果为准。浏览器／跨端／并发／同条件性能、其它复杂关系及MySQL首版总验收继续。

## A33 正式失败、独立清理与夹具首因

用户直接答复call_4a5a1b66ca24443db88c7634b168371a后，固定8fdf784／207e26147134d5ff执行一次，正式0通过、1失败、0跳过。实际已执行到普通导出工作簿回读；之前的结构／默认／反查、原真实身份导航、77行typed持久矩阵与原分页／筛选、精确ID与decimal、普通／非冗余CRUD和父范围、统计抵消／最小值／溢出拒绝，以及两次首行实际DML后整批回滚均按固定顺序通过。两个原事务钩子回执均为0410000000000000、insideTransaction=true；整条仍失败，不能把已到达的前置检查当作完整场景通过。

失败在VerifyExport普通工作簿amount：断言期望0410000000000000，实际040FFFFFFFFFFFFF。保留工作簿及原固定源码复核发现，前面为验证派生筛选已将ordinary-default关联目标更新为下值，导出前没有恢复；旧断言却继续要求上值。工作簿amount／相邻金额均为下值，与当时夹具声明状态一致。后续原SheetJS、数值再导入、非冗余导出／再导入、撤权和重启未完成，不记通过。

执行器的新连接及独立新SSH确认唯一库metarill_fresh_207e26147134d5ff_agent、账号mtf_207e26147134d5ff@%不存在，自有宿主停止及storage合成对象清理确认，private-settings／private-ownership已删除。execution为passed=false、cleanupConfirmed=true、leaseStatus=ended、privateSettingsDeleted=true、autoRetry=false；原固定来源／包、失败TRX和工作簿保持，不重用一次性授权。

当前只修正测试样本隔离和导出前检查，保留产品及原位型断言，详见[A34夹具修正](double-export-fixture.md)。后续必须以新固定程序集及精确范围完整复验，不把本次失败改名为通过。
