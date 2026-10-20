# 安全realm真实RR回归准备

本片仅新增真实MySQL回归源码和复用夹具接线，已编译不等于运行。未执行数据库读写、DDL、浏览器或压力测试，不关闭角色Runtime Gap。源基线9909c21；后续由根集成者完成关联索引显式预览、执行、核验，再分配Server200独占窗口执行。

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

先由根核对现有私有test.settings确实对应Server200，并确认显式索引计划已完成。测试集合DisableParallelization仅限制本程序集，不能代替跨进程独占数据库窗口；不得与API/Metadata/Generation等其他实库套件并发。

```powershell
& E:/git/metarill/.tools/dotnet/dotnet.exe test backend/tests/Metarill.Agent.Tests/Metarill.Agent.Tests.csproj --no-restore -warnaserror --filter 'FullyQualifiedName~SecurityRealmRrMySqlTests' --logger 'trx;LogFileName=security-realm-rr.trx'
```

此命令尚未运行。只有三个真实用例和精确清理均通过，才可报告对应RR边界达标；它们也不单独证明整个角色控件、所有配置写入口或性能无退化。
