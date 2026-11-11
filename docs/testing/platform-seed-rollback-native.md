# 平台种子末段真实故障与恢复验收

2026-11-01，A22；固定c4347a4已获明确授权并完成真实MySQL／Production故障与恢复，1通过／0失败／0跳过；两库／账号／所有权独立核零、凭据删除。A20固定种子合同据此闭环。下方准备记录保留为执行前历史，最终结果见文末；不代表MySQL首版整体完成。

## 必须验证的真实边界

使用固定当前Production的Metarill.Api和正式--initialize命令，沿用生产BootstrapService、MetadataRepository.SeedAsync及SqlRowStore。仅在本轮独占随机平台库中通过生产SchemaService显式创建23表；业务库起始0表。不修改生产代码、不插入自定义故障开关、不使用内存ADO证明实库回滚。

| 阶段 | 真实故障与验收要求 |
| --- | --- |
| 平台目录失败 | 给config_index_column加只拒绝平台最后种子ID的临时CHECK；进程必须明确报该约束被违反，不能把连接／DDL／其它异常当作预期失败。该目录1506条中的最后一条应失败，独立连接确认23张平台表全0行，业务仍0表 |
| 示例目录失败 | 移除本轮首个约束后，在config_menu给示例最后非空种子行加第二个CHECK；生产命令先提交平台1506条，再因示例第25条失败。逐类核对平台保留、示例全部0，账号／角色／项目／设备仍未创建，业务仍0表 |
| 移除故障恢复 | 移除本轮第二个CHECK，再运行原初始化；平台目录完整行摘要保持，示例补全，11类1531种子及业务完整12表无结构差异 |
| 再次初始化及身份 | 种子完整行摘要不变，原账号密码真实Web登录与超级权限成功 |
| 证明及清理 | 固定来源与宿主集合再次校验；本轮凭据、两个库／账号按同轮所有权清理，独立连接及新SSH核零 |

示例目录没有索引种子，其最后非空集合为菜单，不能套用平台的末索引字段故障点。故障点从实际发布目录及生产顺序计算，离线核对平台前缀1505条、示例前缀24条。约束仅作为本轮测试故障；产品表结构仍由元数据和SchemaService创建，不手写实体或改现有平台库。

生产按每份目录独立事务提交。平台失败时23表DDL保持；示例失败时已成功提交的平台目录保持。这是预期的恢复边界，不宣称MySQL DDL回滚、跨目录原子初始化或跨数据库分布式事务。当前产品和原页面／样式／模板未改，原776份保护文件字节保持。

## 专用入口与证据

正式FQN：Metarill.Generation.Tests.FreshInstallMySqlTests.Platform_seed_late_failures_rollback_each_catalog_and_recover_in_production_host。

只接受METARILL_PLATFORM_SEED_ROLLBACK_NATIVE_SETTINGS和METARILL_FRESH_INSTALL_SETTINGS同一路径、platform-seed-rollback-native-v1用途、approved/running及本轮随机编号；拒绝旧成功安装／助手用途、pending、not-started、ended和错误编号。固定生产宿主完整集合及四份发布元数据沿用既有校验，证据路径独立为.local/platform-seed-rollback-native/<run>/evidence，不能复用A21或C88已结束租约。

成功和预期失败共用原进程超时、秘密脱敏与私有日志。正式用例逐阶段写platform-rollback.json、demo-rollback.json，只有恢复／重复／真实Web及固定文件复核全部通过才写verified.json。失败时保留首因和已完成检查点，凭据清理失败另记；不自动重放。

私有离线结果：.local/platform-seed-rollback-native-20261101/preparation.log及results/preparation.trx。实际.NET10.0.402使用独占--artifacts-path、Release、-warnaserror、--disable-build-servers，定向运行PlatformSeedRollbackNativePreparationTests与PlatformBootstrapNativePreparationTests；无真实数据库配置或外部测试对象。

后续须先从提交独立固定源码、生产宿主和正式程序集，核对精确Server-200实例及新对象不存在，再取得本轮双库／临时账号及清理授权。未执行的正式故障、回滚、浏览器、跨端、并发、性能均不计通过。

## 2026-11-01 固定当前来源与精确新范围

c4347a40ebe3f23155b042ea2c220e8e576974bb已推送并独立固定2318源码、65生产宿主文件及152正式输出；Release严格构建零警告错误，固定程序集14护栏再次通过（与前14重叠，不累计）。固定来源、完整宿主、元数据、执行器及租约脚本摘要全部核对，--check-only无子进程／数据库写入。

外层4种失效租约拒绝且外部调用0；实际正式FQN以pending、ended及旧管理用途3次运行，均在连接前按预期拒绝，不计真实事务验收。只读核对Server-200 MySQL8.4.11及既定UUID，确认本轮两个新库／账号／所有权记录均不存在。

本轮编号da4fc62579c2cffe，精确范围为metarill_fresh_da4fc62579c2cffe_platform、metarill_fresh_da4fc62579c2cffe_business及mtf_da4fc62579c2cffe@%；只限这两库的建表、两个临时末段CHECK、种子／账号样本、恢复和同轮清理，不修改旧库。计划.local/platform-seed-rollback-native-20261101/permission-plan.md，scope摘要db3a4a521b2bc501046d2999ea1fcac1bd32f21c1093320dea3952b5449fdcc3，仍pending／not-started。旧A21／C88已结束的精确租约不覆盖新名字及故障用途，正式尚未执行。

证据：source-snapshot.json、fixed-build-state.json、prepared-receipt.json、formal-assembly-files.json、fixed-preparation.trx、pending-guards.json、pending-0/1/2.trx、readonly-preflight.json与protected-files-result.json。原776份保护文件保持。未生成批准记录、临时数据库密码或真实宿主凭据；未执行的故障／回滚／恢复仍标待验。

## 2026-11-01 A22 正式通过及清理

用户call_b053f45d7811454ab8820b9e2d1ddbe3明确批准da4fc62579c2cffe两库／账号／临时凭据范围后，固定c4347a40ebe3f23155b042ea2c220e8e576974bb的正式FQN单次执行：1通过／0失败／0跳过，约1分15秒。未修改固定生产宿主、元数据、SQL异常检查或断言，不复用A21／C88授权，不自动重试。

| 正式步骤 | 实际证据 |
| --- | --- |
| 平台末段故障 | config_index_column最后种子ID2200000000000000912由本轮CHECK明确拒绝，前缀1505条在同目录事务；独立连接确认全部23表0行，业务0表，23表DDL保留 |
| 示例末段故障 | 移除首约束，config_menu最后示例ID2200000000000100031由第二个本轮CHECK明确拒绝；平台1506条种子按11类完整保留，示例25条全部回滚，项目／角色／账号／设备0行，业务0表 |
| 移除故障恢复 | 再移除第二约束，原Production --initialize恢复；平台完整行摘要保持，全部11类1531条种子，23平台表／12业务表反查无结构差异 |
| 重复及真实身份 | 再次初始化完整种子行摘要不变；原表单实际账号密码登录成功，超级权限成功；没有注入Web身份 |
| 清理及独立核零 | 同一实例／所有权清理本轮两库、临时账号及远端所有权记录；新连接及另起SSH再次确认均不存在，租约ended，不可复用 |
| 凭据与文件 | GUID宿主密码配置、外层连接配置／所有权秘密删除；2318固定来源／65宿主／152输出及原776份保护文件保持 |

A20固定平台／demo种子参数、幂等、编辑／墓碑保留与末段回滚由原离线参数证明、A21完整首次安装／幂等及本轮两次实库故障形成完整证据；各批次数分别记录，不将三者混计为一次全量验收。MySQL DDL留存、目录分别提交以及业务库后建顺序如实保留，不宣称DDL回滚、跨目录原子初始化或跨库事务。

私有证据：.local/platform-seed-rollback-native-20261101/da4fc62579c2cffe下native.trx、test-result／verified／platform-rollback／demo-rollback／execution／cleanup／independent-absence／credentials-cleanup；固定evidence内两次预期数据库CHECK错误脱敏日志、初始化和真实宿主日志保留；formal-closure.json、readonly-final-zero.json及delivery-protected-after.json再独立核对。原pending计划单独归档。

本轮没有修改旧开发库六varchar／703空格、当前产品／前端／模板，也未部署或执行浏览器、真机、并发、性能和其它数据库验收；其它初始化／发布场景仍按原进度表管理。

## V1055 当前发行包两类目录末条种子回滚

2026-11-11。使用 abbe89fb 平台发行 ZIP 的 49 个实际 Linux x64 托管宿主文件和同提交的 3,103 份固定源码。在 Windows 本机的 Production 宿主连接 Server-200 的两座独占 MySQL 8.4.11 测试库和隔离 Redis。8 项准备保护及 1 项正式用例通过，零失败、零跳过。

| 场景 | 实库结果 |
| --- | --- |
| 平台目录末条 | 在第 1506 条种子触发本轮 CHECK。平台 24 张表的种子全部回滚，已建结构保留，业务库仍为 0 表。 |
| 示例目录末条 | 在第 25 条种子触发本轮 CHECK。示例目录全部回滚，已提交的整份平台目录摘要保留。账号、项目、角色、设备均无提前写入，业务库仍为 0 表。 |
| 恢复 | 移除测试约束后，显式初始化恢复 11 类 1,531 条种子和业务库 13 张表。双库结构复查零差异，重复初始化后的种子摘要一致。真实密码登录和后台权限回读成功。 |
| 独立回收 | 两个测试库、账号、Redis、SSH 转发、本机监听及临时凭据均核清。 |

两次失败均由各目录最后一条实际写入的 MySQL CHECK 触发。本轮验证目录事务及安装恢复，已经提交的结构与其他目录保留。

## V1158 当前发行包两类目录末条种子回滚

2026-11-12。使用 e682cd12 平台发行 ZIP 的 49 个实际 Linux x64 托管宿主文件和同提交的 3,104 份固定源码。在 Windows 本机的 Production 宿主连接 Server-200 的两座独占 MySQL 8.4.11 测试库和隔离 Redis。8 项准备保护及 1 项正式用例通过，零失败、零跳过。

| 场景 | 实库结果 |
| --- | --- |
| 平台目录末条 | 在第 1506 条种子触发本轮 CHECK。平台 24 张表的种子全部回滚，已建结构保留，业务库仍为 0 表。 |
| 示例目录末条 | 在第 25 条种子触发本轮 CHECK。示例目录全部回滚，已提交的整份平台目录摘要保留。账号、项目、角色、设备均无提前写入，业务库仍为 0 表。 |
| 恢复 | 移除测试约束后，显式初始化恢复 11 类 1,531 条种子和业务库 13 张表。双库结构复查零差异，重复初始化后的种子摘要一致。真实密码登录和后台权限回读成功。 |
| 独立回收 | 两个测试库、账号、Redis、SSH 转发、本机监听及临时凭据均核清。 |

两次失败均由各目录最后一条实际写入的 MySQL CHECK 触发。本轮验证目录事务及安装恢复，已经提交的结构与其他目录保留。
