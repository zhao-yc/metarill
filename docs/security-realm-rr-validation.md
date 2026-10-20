# 安全realm真实RR回归与证据

三个安全关联索引已经由根沿原Schema流程执行并核验。共享实例早期RR失败及临时授权撤销事实保留；后续74889d5在Server-200独立临时实例实际RR3/3，详见末节，不重启或修改共享实例。原f857前置切片只增加离线反例，不能当作真实锁证据。独立生成Host Runtime及其余发布门槛仍不关闭。

## 三个真实场景

1. T1先通过普通SqlRowStore查询建立InnoDB RR旧快照，T2持项目/后台聚合锁；T1实际生成Role Service更新等待该项目主键。只读观测表必须证明等待双方真实连接。T2经实际RolePermissionBusiness撤销普通操作者编辑权并提交；T1锁后current-read拒绝，普通SELECT仍可看到旧快照，目标物理全行hash不变。意外允许写入会使测试失败并强制外层事务回滚，不会把人工回滚当产品拒权成功。
2. T1旧快照仅发现目标账号旧后台A；T2实际Manager Service把该自有账号迁入同项目自有后台B并显式roles=[]。等待后T1必须YZP_CONFLICT。捕获冲突后暂缓外层回滚，观测实际已授予主键锁：必须有A、没有B且没有不可解释的NULL记录锁；随后允许回滚，最终全行hash必须等于T2合法提交结果。
3. 无关Demo后台账号和其自有授权行被独立事务持续持锁；平台真实Role删除引用检查和另一真实工作台保存均应在该锁仍存在时提交。用数据库锁记录及最终持久化结果证明完成，不用Task未完成/ORDER BY文本作锁证明。

前两项各有两条独立业务连接和只读monitor；第三项有三条业务连接及monitor；删除和工作台连接取得真实connectionID后互等ready，断言与holder三者不同，再进入实际业务，保证三条事务确实重叠，允许同realm聚合项目锁正常串行。每条事务断言实例、数据库与@@transaction_isolation=REPEATABLE-READ；不修改全局隔离级别或采集配置。

## 索引与观测前置门禁

在创建任何业务夹具行前，仅只读SHOW INDEX核验manager.admin、role.admin、role_permission.role前缀索引存在。每case实际夹具建立后、启动并发前，再用真实参与的所有后台和角色ID，执行与产品同全列投影/相同归属WHERE的EXPLAIN并记录逐参数计划；不以0或尚未插入的随机role计划代替实际路径。缺失、全扫或选择无关索引明确失败并保留诊断，不跳过、不自动同步结构、不加FORCE INDEX、不重试业务。索引存在本身也不代表优化器一定使用。

参考[MySQL data_lock_waits](https://dev.mysql.com/doc/refman/8.4/en/performance-schema-data-lock-waits-table.html)和[data_locks](https://dev.mysql.com/doc/refman/8.4/en/performance-schema-data-locks-table.html)：前者描述等待与持锁依赖，后者PRIMARY记录锁数据可用于核对主键；LOCK_DATA也可能不可观测。测试不解析内部锁ID格式；观测权限不足、NULL记录或等待证据缺失均保留失败，不能宣布已通过。

## 夹具与清理边界

复用RoleSecurityFixture的真实认证、AddRealmWriteCoordination和AddGeneratedServices；每个直接Service请求使用独立IHttpContextAccessor实例承载已真实认证的上下文，避免多个静态AsyncLocal上下文串线。没有替换产品授权、生成Service、Mapper或协调器；TestServer原认证管线保持原样。

每用例最多三组随机账号/角色/session，最多一个额外后台、两个额外无账号引用角色。额外行创建前记录随机主键和唯一名称意图；清理先验证名称归属，授权只按自有role外键清理，不触config_*、四个服务或既有37图。先取消并排空本用例所有事务，再清理并逐自有主键/外键核验零残留；事务未排空或归属不明时保现场，不与仍运行的写并发清理。额外清理使用独立Session并核对同实例，不复用已经关闭的actor Session。

私有证据在.local/security-realm-rr/<场景-随机ID>/receipt.json，仅包含连接/锁元信息、实例指纹、随机主键、索引计划摘要、全行hash、异常类型和清理结果，不包含密码、token、连接串、SQL正文或整行数据。原异常与清理异常并存时聚合报告，不用清理错误覆盖原失败。

## 执行要求

原准备要求先核对私有settings确实对应Server200及显式索引计划；下列命令只展示过滤器形式，不携带隔离配置。共享实例采集关闭，不直接重跑该环境；成功批次使用独立实例私有settings和冻结源码归档，按已审专用helper控制执行与清理。测试集合DisableParallelization仅限制本程序集，不能代替跨进程独占数据库窗口；不得与API/Metadata/Generation等其他实库套件并发。

```powershell
& E:/git/metarill/.tools/dotnet/dotnet.exe test backend/tests/Metarill.Agent.Tests/Metarill.Agent.Tests.csproj --no-restore -warnaserror --filter 'FullyQualifiedName~SecurityRealmRrMySqlTests' --logger 'trx;LogFileName=security-realm-rr.trx'
```

本节原准备阶段尚未完成RR验收；采集/权限均满足的独立实例成功批次见末节。只有三个真实用例和精确清理均通过，才可报告对应RR边界达标；它们也不单独证明整个角色控件、所有配置写入口或性能无退化。


## 观测权限与失败诊断

首次真实执行3项均失败，不能标记RR通过：当前测试principal缺performance_schema.data_lock_waits列SELECT权限。第三项的失败观测又抛出1142，覆盖了更早的业务/断言错误；首项T2观测失败发生在撤权前，项目锁释放后T1接受未撤销的写权限不构成撤权漏洞证据。旧TRX/log/receipt保留，清理回执均完成。

Probe.Open在采集开关确认为ON后检查data_lock_waits、data_locks和threads实际使用列的SELECT ... WHERE 1=0权限，全部通过后才返回观测器并允许创建业务夹具；不自动GRANT、不切账号、不skip、不重试业务，也不读取观测表行。权限不足以静态中文说明停止，原MySQL号仍作为安全故障元信息保留。临时列级观测授权和完成后的撤销由根按用户明确授权独立办理，本测试不执行授权语句。

第三项先保存原失败类型/HResult/MySQL号，每次补充观测分别捕获并继续，最后重新抛原异常。排空时保存每个Faulted任务的静态故障数组，包含T2等辅助事务；不把预期拒权任务自动改为新的失败。原测试失败与清理失败仍聚合报告，receipt不写异常message、SQL正文、连接或凭据。新增离线回归只证明诊断与权限前置调度，不代表实库权限已具备或RR已通过。

## 2026-10-20临时观测窗口后的真实批次

根在用户明确授权的临时观测窗口中执行完整Agent套件：150项实际执行，147通过、3个RR用例失败、0跳过。临时授权精确限定上述三表17列；结束后已撤销：admin端列授权核验结果为空，应用端三条列检查各返回MySQL 1142。该授权与撤销事实不代表RR已通过，也不能把本批失败称为已发生的产品撤权漏洞。

根随后确认同一服务器 `@@performance_schema=0`。仅有17列SELECT权限只能证明查询获准，不能让关闭的采集设施产生真实锁观测数据。当前Probe.Open同一只读身份查询还读取 `@@performance_schema`，必须为1才继续原三表17列的 `SELECT ... WHERE 1=0` 检查；关闭时以固定中文提示停止，尚未创建业务夹具。异常诊断、原始失败保留和清理规则不变。本片不自动GRANT、开启采集、重启服务或改变共享配置。

采集开关和17列权限都通过后，Probe仅用已有PROCESSLIST_ID列执行当前连接的线程计数，必须恰为1；否则仍在业务夹具之前拒绝。这个固定查询不增加授权列，不把空闲时允许为空的data_locks/data_lock_waits当作采集开关证据。

新增纯离线分支反例：OFF时列检查和后续monitor回调调用数均为0；ON时按原三表17列→当前monitor顺序执行，当前连接行数0或2均拒绝，列权限失败则不调用后续monitor检查。它们只验证前置检查顺序，不构造真实连接或锁记录，不补写为并发验收成功。这些门禁继续适用于后续重跑；独立实例成功批次如下，不追改共享实例失败结果。

## 2026-10-20 独立临时实例：实际3/3与精确清理

集成者从冻结`74889d572f5f00a33465ff68003d5d178cb43797`归档、隔离构建并实际执行。辅助Agent只读复核源码断言、TRX、场景回执和初始化快照，没有再次运行RR。Server-200临时实例复用核实的缓存MySQL8.4.11、P_S开启，不复制共享/客户数据、不改正式settings或共享采集。两个新库初始化前均0表，经正式Bootstrap为平台23表/业务4表及3安全索引，初始化后/RR后快照字节相同。

| 场景 | 实际证据 | 参数计划 |
| --- | --- | --- |
| 旧快照撤权 | 两连接真实project主键等待；普通快照仍旧；Role Service撤权后Unauthorized，目标全行hash不变 | 5 |
| 后台变化 | Manager迁移提交后T1明确YZP_CONFLICT；回滚前A锁存在、B锁不存在，无不可解释NULL记录锁；最终全行等T2提交 | 7 |
| 无关realm锁 | 三条不同业务连接重叠；删除和工作台提交、持久化核验后无关账号/授权X锁仍存在，然后才释放holder | 9 |

TRX total/executed/passed均3，failed/timeout/aborted/未执行均0。**21项actualFixtureIndexPlans的chosen均为对应有效前缀索引，访问ref**；三组先排空事务、按自有主键/角色外键清理并核验零残留。不用Task状态、SQL文本或汇总result单独证明成功。

贡献clone私有目录`.local/rr-isolated-20261020/execution/`保存：

- `rr-isolated.trx`，SHA256 `0da9547bef7692b5c99e742a47dbe8753c87a7db1124428b223fa03aae18d005`。
- `before-initialize.json`、`after-initialize.json`、`after-rr.json`；后两份同SHA256 `f7dbf41c4d66dad39de953a77ec7ba9e311ec1673fe5946e8037ea994ef4d76e`。
- `source/.local/security-realm-rr/rr-revocation-*/receipt.json`、`rr-target-admin-change-*/receipt.json`、`rr-unrelated-realm-*/receipt.json`。
- `cleanup-20261020053813.json`证明自有容器、卷及远端运行秘密移除；本机运行settings/secrets已移除。
- `date-safe-tunnel-stop.json`证明只停自有隧道：持执行锁，ISO日期按字符串解析，启动时间/exe/命令摘要和唯一监听严格核对；保持已执行v2输入/seal，无重Run。

长期备用文件按用户备份规则保留，不公开账号、实例UUID、连接或备份内容。共享旧150项仍为147通过/3RR失败，临时17列授权撤销后应用三检查1142；共享采集关闭事实不因新实例成功而改写。

只支持本批三个场景，不等于账号原页浏览器完整写入、独立Host安全Runtime、所有配置入口、任意负载死锁或性能达标；角色selection/export和5个Custom页面门槛继续保留。
