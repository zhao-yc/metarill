# 角色权限工作台核心提取

本片只把原`RolePermissionBusiness`及`RolePermissionGrants`、`RolePermissionSave`合同移到`Metarill.Security`，保持`Metarill.Runtime.Security`命名空间。Security继续只引用Core/Data，不包含ASP.NET、Config、MetadataRepository或Agent控制面。平台原`roles GET`、`snapshot POST`、`save POST`、`R<T>`及完整revision不变。

## 必要宿主契约

`IRolePermissionCatalog.ReadAsync(Session,projectId,CT)`提供普通工作台完整目录；`RequireLoaded(Session,Transaction,resources)`只取得锁后已加载的原完整目录。平台与`ISecurityCurrentResources`都映射同一scoped `PlatformSecurityCurrentResources`。普通读取先证明适配器及仓储使用同一个Session；锁后仍沿原CWT对象身份、实际Session/CurrentTransaction/连接及仓储证明，等值投影、另实例、错库和旧事务均拒绝。缺接口、返回null或异常都不回退发布目录。

身份只从`ISecurityIdentityAccessor`取得已经认证的`AuthenticatedIdentity`，Actor仍来自该Identity。已发布页/表交集使用`ISecurityResourceRegistry`的窄稳定字段，平台真实目录仍经原MetadataRepository读取；锁后仍`config_table→config_page→config_menu→config_button`的当前读，不增加额外读取或改变聚合锁顺序。`SecurityRealmCurrentReader`、MutationGuard及其内部准备状态没有移出或开放。

## 既有revision

原摘要包含角色语义、同后台完整页面/菜单/按钮和全部源授权，不改成仅名称或窄投影。由于平台`JsonDefaults`还服务其它HTTP/配置用途，本片不搬走该全局工具；Security内部`RolePermissionRevisionJson`明确绑定旧revision字节合同，保Web命名、缩进、long字符串和`yyyy-MM-dd HH:mm:ss`日期格式。测试逐字对照原JsonDefaults并执行实际Revision方法，覆盖完整目录、长ID、日期、Extra/null、字典顺序及非ASCII。

## 验收和边界

初始提取源码片仅离线编译与固定内存SQL传输测试，当时不执行HTTP、数据库、结构变更或服务启动；后续根集成实库批次见下节。既有工作台的同后台页权、普通编辑者、update-only回执、旧revision冲突、跨后台拒绝、软删除及协调器当前读继续回归。

默认Full宿主、打包模板、更新器及前端不变；Security源码中存在工作台不代表其已注册或独立可用。Full没有新增不可变目录适配、管理页或三个平台安全Service手写partial。Runtime A与五处Custom门禁保持，不能用本片测试替代新宿主MySQL/RR或原页面浏览器验收。

初始提取源码片最终离线结果：Agent工作台/当前读/协调/接线及新合同108/108，独立Security13/13，完整包profile与旧目标升级8/8，均0跳过并启用警告即错误。平台API、正式CLI及CLI重新装配的490文件完整包自身Release均0警告/0错误；包没有Metarill.Runtime项目引用。三个测试程序集的129个case分别计数，不重复累计首轮106项。

精确Agent filter为`RolePermissionCoreExtractionTests|RolePermissionRevisionCompatibilityTests|RolePermissionRealmTests|SecurityCoordinationExtractionTests|SecurityRealmWiringTests|SecurityRealmMutationGuardTests|SecurityRealmCurrentReaderTests`（每项使用FullyQualifiedName包含匹配）。Security执行该独立测试工程全部13项；Generation仅`ApplicationAuthenticationPackageTests`。私有回执位于本任务`.local/role-permission-core`，初始提取源码片未运行任何实库Fact或HTTP宿主。

## 2026-10-20 根集成批次

提取源码 `4d4ec20729c342712655dd7d5621f531a76e4e6e` 集成为根冻结 `d6d0594307682d19107b791a9cf213daeead6321`。主协调者在平台集成配置中实际执行以下批次，均与前述贡献clone的108/13/8离线结果分开保留：

| 范围 | 根集成实际结果 | 私有报告（根 `.local/`） |
| --- | --- | --- |
| Security | 13/13、0失败/0跳过 | `role-permission-core-security-integrated.trx` |
| Agent非RR v2完整工程 | 195/195、0失败/0跳过，含22个真实MySQL用例 | `role-permission-core-agent-nonrr-v2-integrated.trx` |
| Full/BackendOnly及升级profile | 8/8、0失败/0跳过 | `role-permission-core-profile-integrated.trx` |
| Api Release | 0警告/0错误 | `role-permission-core-api-release-integrated.log` |

Agent工程同时包含离线合同及真实数据库用例，不能把195项全称为SQL场景。首轮因漏设 `METARILL_PLATFORM_METADATA`，实际173通过、22跳过、0失败，原 `role-permission-core-agent-nonrr-integrated.trx`/`.log` 保留；不是195全通过。随后仅补22的命令因自动审批超时未启动，不能计为已执行；最终改用固定简单filter实际重跑完整195并全部通过，不把173、22和195累加。

本批没有新RR、观测授权、前端、浏览器或设备验收。平台工作台集成实库通过不等于独立Full已注入完整目录权威、协调器、三个安全Service手写partial、安全管理CRUD或Agent；Runtime A与五处Custom等门禁保持，后续SessionPolicy与目录适配应按各自冻结另验。

根综合回执另保留纠错：旧 `.local/role-permission-core-integrated-receipt.json`（SHA256 `706731774d78c7f26ee897f6b0b668c92b1ea595fb6b6503b0b0025790085750`）曾按VSTest `Counters.notExecuted=0`误记首轮skip；原件未覆盖。新版 `.local/role-permission-core-integrated-v2-receipt.json`（SHA256 `7664b447c31dc217424cf831fbf52bbfa2486875c6ff5a881f314bedf1559e74`）按实际 `UnitTestResult`记录首轮22跳过，并保存原rawCounters和纠错说明；四份TRX及原日志不变。上述计数以真实结果项及原日志为准，不把汇总的旧零值当作已执行。
