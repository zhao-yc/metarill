# 安全协调核心提取：第二阶段

本阶段开发基于 `74889d5`，集成 C# 验证冻结为 `6dc09a4`；后续独立 RR 使用归档冻结 `f934a874`。本阶段把真实 `SecurityRealmCurrentReader`、`SecurityRealmMutationGuard` 及最终验证移入 `Metarill.Security`，命名空间继续为 `Metarill.Runtime.Security`。`SqlRowStore` 仍在Data。共享工程仍只引用Core/Data，不引用平台Runtime、ASP.NET、Agent、MetadataRepository、Generation或全量配置目录。

## 必要宿主适配

- `ISecurityIdentityAccessor` 只读取宿主认证管线已经建立的真实完整身份，不接受请求构造的Actor。
- `ISecurityCurrentResources` 必须从指定实际Session/DbTransaction当前读目录及菜单引用。缺失、异常和null返回均明确拒绝；共享Reader没有发布目录fallback。
- `ISecurityCurrentSessionPolicy` 只取得Agent grant锁；`ISecurityLockedAgentGrant` 只提供绑定匹配行为，不提供原grant/token/device/refresh字典。
- `PlatformSecurityCurrentResources` 与其接口在DI中映射到同一个Scoped实例。元数据仓储和菜单引用适配均验证实际Session、当前DbTransaction及有效连接。

## 不改变事务和原规则

原发现锁集合、项目升序、后台升序、grant→session→账号→角色→授权→资源目录、目标行及最终复核顺序保持。工作区仍仅参与锁集合兼容，不替代真实身份后台。

grant字段匹配与刷新有效期求值固定在session锁之后；平台私有proof只保留原判断实际使用的字段，原OAuth实现及表访问留Runtime。无grant、撤销或损坏时间仍明确拒绝。

后台删除/迁项目的最终引用读取继续角色→菜单→账号，并在首个引用短路。菜单存储缺失是错误，不当作零引用。原账号角色省略/null/[]、角色跨后台拒绝、超级标志、软删引用与准备状态检查不变。

`_states`、锁集合、Pending、工作台Prepare和配置守卫准备校验均保持非公共；原供三个手写partial防漏接的静态 `RequirePrepared` 只检查已有准备，不产生新准备。没有新增Core生命周期或公共Prepare入口。

`SecurityPageResource` 追加缺省拒绝的CanCopy/CanImport，并按原IsCopy/IsImport/importMethod非空语义投影；不借CanUpdate代替复制/导入开关。固定平台安全表ID原样保留，本阶段不宣称支持任意生成schema。

## 工作台revision保持原完整JSON

`RolePermissionBusiness` 仍在Runtime，HTTP和revision实现不迁移。当前目录适配只缓存原FOR UPDATE结果；共享核心仅持瘦资源投影。

缓存使用 `ConditionalWeakTable` 的对象身份键，值保存原目录与实际Session/DbTransaction，不回指key。工作台持有本次投影期间原目录可取回；批量CRUD临时投影不会因请求缓存而强留每一份完整目录。

取回时再次验证投影对象身份、实际Session、事务及它仍是CurrentTransaction。等值clone、另一个适配实例、已提交或另一事务均拒绝。不重查、不从瘦DTO重算revision、不回退发布快照。原页面/菜单/按钮名称等完整JSON继续参与同一摘要，SQL请求数不增加；未作性能对照，不宣称耗时/分配等价。

## 验证与交付边界

离线回归包含实际SqlRowStore传输、平台当前目录适配、现有认证/权限/Guard/工作台用例及新增同实例DI、引用缓存、错Session/仓储、缺菜单/适配、原复制导入开关、grant延迟求值与后台引用短路锁序。所有原始账号/会话值不作为新断言正文。

提取源码片本身未修改独立宿主模板、Packager、Full/BackendOnly门禁、元数据、生成产物或数据库记录；开发及只读 peer 没有执行 DB/Docker/DDL/HTTP 部署。随后由集成者执行下列新冻结验证，结果单独记录，不继承旧 `74889d5` 的真实 MySQL/RR 结论。独立应用下发、正式 Host 认证/初始化和角色工作台的完整下沉仍未完成；安全物理模型派生也只准备结构输入，不代表这些宿主能力已交付。

## 集成验证：6dc09a4 与 f934a874 分批记录

C# 代码冻结 `6dc09a4e11f72d46513cddd5a7d8e8428413b723`，Api Release 构建零警告、零错误。下列报告位于主仓库私有 `.local/`，均实际执行、零失败、零跳过；带 MySQL 的工程也含离线合同，工程计数不能当作 SQL 锁场景数。

| 范围 | 实际通过 | 原始报告 |
| --- | --- | --- |
| Security 核心 | 13/13 | `security-coordination-core-integrated.trx` |
| 非 RR Agent | 182/182，明确排除 RR 三项 | `security-coordination-agent-mysql-integrated.trx` |
| Metadata | 152/152 | `security-coordination-metadata-mysql-integrated.trx` |
| API | 71/71 | `security-coordination-api-mysql-integrated.trx` |
| Compatibility | 29/29 | `security-coordination-compatibility-integrated.trx` |
| Generation 定向（含 MySql 类） | 56/56 | `security-coordination-generation-focused-integrated.trx` |
| 另一次 Generation Contract 补验 | 14/14 | `security-coordination-generation-contract-integrated.trx` |

Generation 首次过滤器实际选中 56 项，含 MySql 类但不表示 56 项都是实库动作；随后另补 14 项 Contract，不是此前 69 项的同一批，也不是完整 Generation 全量通过。汇总 `.local/security-coordination-integrated-receipt.json` 的 SHA256 为 `ceef1d5f7d86bf7c3c8569aeb6ee8e06b420931f99ad971df3db95aa77ff12e8`。C# 批次后只合入 Node builder 顺序修复，形成 RR 归档冻结 `f934a87425d0885aab841b7d91ab77bc4017bd62`，没有借未提交 C# 源替换执行。

集成者在 Server-200 新独立临时实例上执行该冻结：P_S ON、正式 Bootstrap 两新空库由 0/0 表到 23/4 表及 3 个安全索引；RR 3/3，零跳过，21 个真实夹具参数计划全部使用相应前缀索引且访问为 ref。三组 owned 清理零残留，测试前后结构同字节；随后原 sealed helper 的 Stop/Cleanup 成功，首次正式走通 DateKind 字符串解析的停止路径，容器、卷、远端运行秘密、隧道及本机运行 settings/secrets 均移除，长期备份按用户规范保留。精确 TRX、结构和清理摘要见 [RR 专项](../security-realm-rr-validation.md) 的 f934 批次。

此前 4/6 是运行中快照；集成者现已核对 f934 的 [PR CI](https://github.com/zhao-yc/metarill/actions/runs/39378960357)（最终更新时间 `2026-10-20T06:32:13Z`）和 [push CI](https://github.com/zhao-yc/metarill/actions/runs/39378955800)（`2026-10-20T06:29:26Z`），两 run 均为同一 f934 head，六个作业最终全部成功。Linux Generation 603 通过/12 跳过、Windows 604 通过/11 跳过，两者总数均 615；CI 缺私有 DB 配置，跳过不算实库验收。完整分项目计数和原始结果回执见 [验收台账](../acceptance.md)。这组 CI 不自动覆盖随后角色用途切片；旧完整 612 批次失败和共享实例 RR 失败原样保留，新阶段也不代表独立 Host 全部完成、任意配置入口撤权安全、跨端设备或 Java 同资源性能达标。

## 随后固定角色用途接线的独立证据

平台固定角色/后台名称的 selection 与 export 用途已由源码 `d6967d96d1464cf3acc10b90cd7d66eca11bb1e4` 接线，正式产物提交为 `e4209a139103d60da43452ec66e63b92cd458b65`，合同说明更新集成点为 `edadf60719907fbc687326889d6f85313a7a5ebf`。两轮 191 个受管文件与归属清单字节稳定，24 个手写 partial 不变，Api Release 零警告/零错误；原角色多值导出仍按原协议输出 ID 数组 JSON 文本，不改成名称拼接。

主仓库 `.local/security-read-uses-generation-integrated.trx` 136/136 与 `.local/security-read-uses-agent-mysql-integrated.trx` 非 RR 182/182 均零跳过。前者包含新增 5 个用途合同，证据是离线真实生成 SQL 和 Excel；新角色选择/导出专项 MySQL case 仍在准备、尚未执行，不能由既有非 RR Agent 实库回归替代。运行前已只读核验共享实例指纹及三条观测权限检查 1142，没有新授权。独立 Host 完整 A 下发和五处平台 Custom 独立宿主门槛仍未关闭；此处只更新平台用途接线状态。
