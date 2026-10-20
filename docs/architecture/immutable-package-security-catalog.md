# 独立Full的固定目录权威

本片只为普通 FullApplication 提供编译绑定的目录来源和两项目录适配，不接通 SecurityRealmCurrentReader、SecurityRealmMutationGuard、RolePermissionBusiness、管理 Controller 或三个安全 Service partial；平台实时目录、Runtime A、五处 Custom 和 Agent 门禁继续保留。

`project.json` 仍由发布脱敏模型生成，固定 UTF8/无BOM/LF 字节的 SHA256 写入 `PackageSecurityCatalogBinding.g.cs` 编译常量。固定运行时代码 `VerifiedPackageSecurityCatalog` 启动先私有复制完整 bytes，再核验该摘要、编码和元数据，不相信可改 manifest 的自报 hash，不热重载；原可变 DI 元数据或返回 clone 的修改不会改变权威。后台、页面及菜单的普通认证投影也从此来源产生不可改集合。

`ImmutablePackageSecurityResources` 的 ISecurityCurrentResources 与 IRolePermissionCatalog 映射同一个 Scoped 实例。普通读取返回完整目录的独立 clone；聚合预锁后的证明只在指定 SqlRowStore.Session 的实际当前且连接打开的 DbTransaction 签发，以 ConditionalWeakTable 记录原资源对象身份、Session、事务、连接、目录世代及摘要。RequireLoaded 不接受等值投影、发布列表、另一适配/事务或已完成事务，返回同一完整冻结代的独立 clone供原 revision 使用。菜单引用从完整权威目录判断，仍核真实事务和已声明后台；不读取不存在的 config_* 表，也不以缺表返回 false。账号、会话、角色、授权行的当前读取和聚合锁并未被静态目录替代。

平台适配继续使用原 MetadataRepository 当前 FOR UPDATE 与完整目录缓存；接口不提供失败后回退 PublishedCatalogs 的路径。此处的不可变目录是完整包显式选定的权威来源，不是平台实时配置读取失败后的替代。

## 更新范围

`PackageSecurityCatalog.cs` 属于固定 sync-runtime；`project.json` 和精确受控的 `PackageSecurityCatalogBinding.g.cs` 同属 generate-project-config，日常配置按钮可以共同原子更新两者，不新增运行时动作。新候选必须拥有与完整 JSON 字节相符的编译绑定源码，重算 manifest 不能绕过；旧 Full 首次缺少实现与绑定时需明确满足 Runtime/Config 依赖。源码更新不执行 SQL、DDL、初始化或构建，激活新目录仍需应用重新构建/启动。手写 partial、私有配置、旧 journal 和 BackendOnly 原开发宿主边界不变。

## 验证边界

用例以真实生成Full包编译后的类型执行：篡改字节/BOM/CRLF、clone隔离、同scope DI/同Session实际事务、伪造和旧proof、固定realm及menu引用；本机ADO连接/事务替身不执行config SQL，不证明MySQL锁/RR。包更新用例验证单Config按钮原子更新、候选绑定拒绝、旧Web包一次双动作升级，以及HostCustomization、手写Service partial和AppID的字节保留。

2026-10-20，基线`d6d0594307682d19107b791a9cf213daeead6321`，认证/包profile/前端闭包邻近106/106通过，0失败、0跳过，其中固定目录命名用例21项，并加强既有profile闭包，包含真实Full包编译；TRX为私有`.local/immutable-catalog-results/immutable-catalog-neighbor-freeze.trx`，SHA256为`efaad58ab16f88e66e20e003fbbbc5ca712528a22066bdbd01db60b5f6055f9b`。先前16/20批次的4项失败是反射构造异常包装断言，保留原TRX，夹具解包后按原拒绝合同通过。

准确`LocalGenerationUpdateTests`全类首轮64项为62通过、2失败、0跳过，原TRX`immutable-catalog-updater-freeze.trx`（SHA256 `948239227c42317ea3dc318b17bb19ecfd036e2f1502701e961b24af87644a87`）保留。两项为readonlyCustom/cycle候选夹具手动重建完整模型却未同步新增Binding与摘要，被严格目录校验提前拒绝；仅补夹具同步同一实际project字节摘要、编译Binding及外manifest归属，不改产品或原反向预览/最终具体诊断/字节不变断言。补测这2项及固定目录21项，23/23通过、0跳过，TRX`immutable-catalog-capability-binding-v2.trx`（SHA256 `f317fbbcf1442bc9e12af8e0fe1df72be764a289eef6e133e2264f84006e1b8d`）。未重跑整64，不将重叠用例累计为新验收量。工厂与typed Payload的manifest字段顺序差异由独立CI切片修复，合并后还需完整再验。本片未访问数据库、HTTP、浏览器或设备，也未证明安全Runtime A已下发。

## 后续安全管理接线边界

当前菜单引用适配要求目标后台已经由包目录发布。若后续原后台页允许新增同项目但尚未发布的后台再删除，不能把该后台直接当成包外realm，也不能将未知后台解释为无菜单引用：须先在同事务锁读真实后台并证明项目归属，再由完整冻结目录判断菜单引用。本片尚未开放安全CRUD，保留当前未知realm拒绝用例；该动态后台场景留待Runtime A接线时按原规则验收。
