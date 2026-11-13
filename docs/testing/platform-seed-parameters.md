# 完整平台种子的参数与幂等合同

2026-11-01，A20；来源796c34c。只新增测试和文档，未修改初始化、持久化或发布元数据。真实MySQL空库安装另验。

**当前补充：A21已于2026-11-01在Server-200以固定4dea24a生产宿主完成真实双库空库安装，平台23表／业务12表、11类1531种子、重复保留编辑／墓碑／密码及真实Web／重启会话1/1通过，清理独立核零。A22现以固定c4347a4真实Production宿主完成平台／示例各自末段CHECK失败、逐目录回滚和恢复，1通过／0失败／0跳过，独立清理核零。下文保留原始离线证据，不将其回填为实库结果；A20固定种子合同现已闭环。** 见[完整首次安装](platform-bootstrap-native.md)及[A22末段故障正式结果](platform-seed-rollback-native.md)。

通过生产 `MetadataRepository.SeedAsync`、`SqlRowStore` 与 `MySqlSchemaPlanner.Compile` 执行平台和demo完整种子；仅ADO传输替换为原测试内存驱动，ORM禁止自动同步，不能称实库事务或DDL验收。

| 检查 | 实际结果 | 边界 |
| --- | --- | --- |
| 两份完整目录 | 11类元数据合计1531条，逐表数与发布目录一致 | 不包括初始化账号／角色、文件对象或业务CRUD |
| 实际INSERT参数 | 按生产规划器逐列核对必填／可空、bigint／int／日期／字符串类型、varchar字符长度与正整数主键 | 不证明MySQL默认值、排序规则或唯一约束 |
| 六个内容关联ID | 3351格逐条核对：传出的非空值为正long，NULL或省略仅允许可空物理列 | 新库的省略／空值规则；不是清理存量703个空字符串格的授权 |
| 重复种子 | 平台与demo再次执行只有SELECT，全部行逐字保持 | 不覆盖已有编辑，不复活墓碑 |
| 末表失败 | config_index_column故障注入，同一目录之前写入全部回滚 | 内存事务快照；不证明MySQL DDL回滚或跨库事务 |
| 定向回归 | 5通过／0失败／0跳过，其中3新增、2既有 | 与全量重叠，不累计 |
| 元数据全量 | 554通过／0失败／25条件跳过 | 25跳过未计实库通过 |

首轮MSBuild子节点提前退出，未获得测试终态；确认原进程结束后，直接使用已编译程序集得到4通过／1失败。失败来自新测试把合法省略的可空引用误计为必须传出；修正为逐格检查物理可空性、实际类型并核对精确3351格，再严格串行构建及定向、全量验证通过。原构建中断与断言失败日志保留，没有修改生产规则让测试通过。

私有证据位于 `.local/platform-seed-20261101/`：`focused.log`、`focused-retry.log`、`focused-final.log`、`metadata-all.log`及results下对应TRX。A19确认的开发库六列旧varchar和703个空字符串格仍未改动；空库正式安装、Web登录、结构同步、生成、业务CRUD和同条件性能仍需真实验证。


## 2026-11-01 当前空库验收夹具跟进

起点f58677d。`BootstrapService`已通过`ProjectPhysicalModels`创建业务、文件、安全和Agent完整物理结构，而原`FreshInstallMySqlTests`只把demo与文件基础表合成预期；这会在正式空库初始化后误报表数和结构差异。本次让原夹具共用生产的完整物理模型，并将首次及重复种子计数从四类配置扩展到`MetadataRepository.SeedAsync`实际写入的全部11类。未修改初始化、种子、ORM、DDL或发布目录。

独占`--artifacts-path`严格编译后，现有实际独立宿主物理图对照首轮因共享测试夹具从bin相对层级找错metadata目录而失败。`TrustedSecurityServiceFixture`改为使用编译源码位置确定backend根，不以输出目录或环境选择可信模板；原失败日志保留。修正后物理图对照1项通过，受影响的实际生成管理Service partial及权限工作台40项通过，均0失败／0跳过。没有新建镜像测试或降低结构门槛。

本批只有验收代码变化。真实平台空库用例没有执行，不代表MySQL种子、DDL、Web登录或事务通过；A20仍为部分完成。C82产品1e57bb9／执行器7514ada的固定来源、990文件包及pending租约未修改、未消费，亦不把本批测试计为C82正式验收。原776份产物／前端／模板／元数据逐字保持。

证据位于`.local/platform-bootstrap-expectation-20261101/`：`physical-graph.log`及首轮TRX、`physical-graph-retry.log`、`trusted-fixture.log`及对应终态TRX。实际命令为固定.NET SDK对Generation.Tests使用`-c Release --artifacts-path ... -warnaserror --disable-build-servers`运行原物理图对照，再用该独占程序集运行`TrustedSecurityServicePartialsTests`及`TrustedSecurityWorkbenchTests`；没有设置真实MySQL租约。
