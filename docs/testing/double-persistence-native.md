# double正式持久化验收准备（A30）

日期：2026-11-02。产品起点：`f5ed1551a4837285401f95550c53595644eca087`。本项为[MySQL首版节点](../mysql-first-release-milestone.md)补齐A17／A23—A29尚缺的真实持久表证据。**当前已实现测试驱动及离线完整包准备，正式新库场景尚未执行，不能记为持久化通过。**

## 同一份元数据与实际完整包

复用已有非冗余三表模型，实际生成普通party、非冗余order和group，保留项目54／后台5、原List关联、派生customer.amount和真实group_id=@parent_id父范围。元数据统一声明double，新增四个物理浮点默认出口及一项精确decimal对照；不手写Entity／Mapper／Service或业务DDL，不修改原页面／模板／接口。

| 表／字段 | 类型 | 默认值位型 |
| --- | --- | --- |
| mt_double_native_order.measurement | double | 0410000000000000 |
| mt_double_native_order.measurement_previous | double | 040FFFFFFFFFFFFF |
| mt_double_native_party.amount | double | 0410000000000000 |
| mt_double_native_party.previous_amount | double | 040FFFFFFFFFFFFF |
| order.customer_amount | 派生double，来自party.amount | 不进入物理列 |
| party.exact_amount | decimal(30,9) | 另用超JavaScript安全整数范围的小数对照 |

通过公开ApplicationPackager创建真正FullApplication／web-database-v1包，不使用BackendOnly或改造Program。普通与非冗余页均声明原导入／导出及double统计；两份手写partial只用于观察真实首行DML后拒绝第二行，正规再生成两轮保护它们。实际包已独立严格构建，当前Core／Data／Exports关键数值源码一致，原运行时JS字节保持。

## 已编译的正式入口

`Metarill.Generation.Tests.DoublePersistenceNativeMySqlTests.Production_web_double_defaults_crud_filters_statistics_excel_and_rollback_keep_binary_values`。

驱动按以下顺序实现，尚未真实执行：

1. 专属用途／批准／运行中租约、产品基线、固定准备根、三表名单及512条样本预算先校验；原SheetJS依赖缺失同样在复制、宿主和连接之前拒绝。旧B121租约不能启用本用途。
2. 实际Production宿主禁用DevelopmentBootstrap，普通启动／预览保持空库，显式结构安装及重复零差异；预期本模型3业务表＋11基础表。结构预览、默认反查草案与往返通过原SchemaService／规划器。
3. 通过缺列SqlRowStore INSERT读取数据库实际默认值，逐位核对四个默认；不以C#实体默认值代替COLUMN_DEFAULT。真实管理员初始化密码保持、原三字段普通账号登录，菜单与页动作分开授权。
4. 普通生成CRUD验证默认、相邻值更新、省略与null；真实typed仓储矩阵含正负极值、最小值、已知相邻值、64个固定随机值及boxed float；ID和decimal对照保持。原分页、数值等值筛选及非冗余派生／父范围、复制／软删通过实际生成接口。
5. 普通及非冗余统计分别验证Max合法抵消后为1、两个最小值相加、Max+Max最终溢出拒绝；JSON非有限／溢出／非零下溢拒绝及无业务写入。
6. 普通和非冗余导入第二行手写规则必须实际读到第一行DML及原位型、当前事务后才故障，整批三表摘要保持。快照使用当前可靠JSON，不能因旧格式本身合并相邻值而掩盖变化。
7. 真实下载XLSX、数值类型／相邻位型及派生值回读，执行原SheetJS选择方法。只把原回调中已开放的数值列送入生成导入器，派生列原只读门禁保持；不声称所有导出只读列都能原样提交。
8. 真实普通账号导入／导出撤权后无写入，同地址重启保持会话、注销拒绝与结构零差异；固定包不启动，实际执行在独占副本中。停自有宿主后清理本轮storage/GUID，数据库及账号由未来精确外层租约按所有权清理并独立核零。

原数值HTTP请求用JsonDefaults.Options编码，防止测试客户端的默认double格式先丢位；分页Total按原StringLong转换器的字符串Token核对。没有为测试更改产品协议、权限或统计语义。

## 当前实际验证

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

## 尚未完成及下一步

独立冻结来源／程序集／Full包及Node／原SheetJS依赖已完成，见下方固定批次；外层单次协调器、精确新库／账号／存储清单和只读实例／对象预检仍须完成，然后才请求本轮资源授权。当前没有新建库／账号，没有连接Server-200或执行DDL／业务DML，也没有正式测试结果；不借用已结束B121租约。

后续正式完成后也只关闭该固定数值三表子集。全部复杂关系、浏览器、跨端、并发及同条件性能仍按进度表验收，不因此宣布MySQL首版完成。
## 独立固定批次完成（57cc81f）

从57cc81fad166ef5d201a6c12ec29e1e2f8d75d7f建立独立普通克隆，2384份跟踪源码／152份正式输出／947份实际Full包文件已逐项固定；严格Release零警告／错误，新固定程序集的16项准备／门禁全部通过、0失败／0跳过。该批仅运行2个准备和14个租约测试，没有运行正式新库用例，与前述批次不累加。

按原package-lock以ignore-scripts安装27项Node依赖，没有改动锁文件；Node v24.17.0真实可执行文件、xlsx.mjs、锁文件、原选择方法JS及原Excel探针文件均保留独立摘要供后续外层协调器绑定。不能只固定C#包而让实际原解析器漂移。

首次离线汇总包装器误沿用B121的“不得存在公开Full标记”检查，在16项已通过及真实Full包已建立后汇总断言失败。A30本来就要求真正Full，因此校正包装器为必须存在.metarill-package.json，再纯文件核验FullApplication／web-database-v1／专属namespace、全部源码／程序集／包文件与16项TRX并完成固定；没有修改包、重新构建或重跑准备来掩盖失败。原失败说明与日志保留，该次不是数据库或业务失败。

固定副本保持只读，后续业务仅在独占执行副本运行。外层协调器及可审查精确资源计划尚未完成，未创建新库／账号或执行DDL／业务DML，未申请或消费新租约；A30保持待正式验收，不能标记完成。