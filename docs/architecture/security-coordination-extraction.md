# 安全协调核心提取：第二阶段

本阶段基于 `74889d5`，把真实 `SecurityRealmCurrentReader`、`SecurityRealmMutationGuard` 及最终验证移入 `Metarill.Security`，命名空间继续为 `Metarill.Runtime.Security`。`SqlRowStore` 仍在Data。共享工程仍只引用Core/Data，不引用平台Runtime、ASP.NET、Agent、MetadataRepository、Generation或全量配置目录。

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

本片不改独立宿主模板、Packager、Full/BackendOnly门禁、元数据、生成产物或任何数据库记录，不执行DB/Docker/DDL/HTTP部署。独立应用下发、安全schema适配和角色工作台的完整下沉仍未完成。根748的真实MySQL/RR结果只属于旧冻结版本，本片必须由集成方另行重验，不能继承该结论。
