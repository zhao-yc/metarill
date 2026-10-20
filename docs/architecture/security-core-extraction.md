# 公共安全核心提取：第一阶段

本阶段只把平台真实认证与权限决策提取到 `Metarill.Security`，不是独立应用安全运行时下发完成，也不关闭角色能力缺口。

## 工程边界

依赖方向为 `Metarill.Runtime → Metarill.Security → Metarill.Core / Metarill.Data`。新工程不依赖平台 Runtime、ASP.NET、Agent、Generation、Files 或 Exports。

`AuthenticationService`、`AuthenticatedIdentity`、`PasswordHasher`、`PrincipalRolePolicy` / `PrincipalRoleRealm` 的命名空间继续为 `Metarill.Runtime.Security`，实际程序集改为 `Metarill.Security`。源码调用方随引用图重新编译；本阶段不承诺旧已编译二进制无重建替换。

`SqlRowStore` 仍由 Data 工程提供（历史命名空间仍为 `Metarill.Runtime.Persistence`），没有另复制一份持久化实现。密码摘要、令牌摘要、角色ID解析、原账号状态与后台归属判定保持原实现。

## 平台适配

- `ISecurityResourceRegistry` 只提供固定资源投影：项目、表/页/菜单/按钮ID、归属及页面动作开关。它不提供连接、字段、查询SQL、页面布局或平台配置写入能力。
- `MetadataRegistry` 提供平台与示例的原发布资源；`PlatformSecurityResourceProjection` 保持原 `PageUpdatePolicy` 和 `exportMethod` 解释，不以工作区选择替代真实后台身份。
- `ISecuritySessionPolicy` 由宿主提供Agent会话附加证明，仅接收grantId、accountId、projectId三个真实绑定值，不传完整账号或会话字典。`PlatformSecuritySessionPolicy` 保留原批准状态、ClientId、账号、项目和刷新有效期校验，以及原单次grant读取；共享工程不引用OAuth服务或grant表。
- API只新增两个平台接口映射；原认证服务生命周期、HTTP中间件、权限响应、Guard注册先后与路由不变。

发布目录投影在原决策点进行，增加内存投影对象，不增加数据库请求；本片没有性能对照，不宣称分配或耗时不变。

## 仍保留在平台的能力

`HttpRuntimeAuthorization`、`SecurityRealmCurrentReader`、`SecurityRealmMutationGuard`、`RolePermissionBusiness`、配置Guard及角色授权revision的JSON约定仍属于平台Runtime。

锁后读取仍按原项目/后台预锁、grant→session→账号→角色→授权→当前目录顺序执行。唯一接线变化是已加载的当前目录先转窄资源投影，再交共享决策；没有公开锁集合、准备状态或新增Service生命周期。

普通认证与锁后复核仍复用同一个内部决策函数。共享工程只向平台Runtime和独立核心测试开放内部决策，不新增HTTP可调用的授权旁路。

## 未改变的交付边界

本阶段不改独立宿主开发认证、应用Packager、Full/BackendOnly能力门禁、元数据、任何生成产物或已有手写partial。没有自动建表、Seed、账号初始化、默认凭据、数据迁移或运行时热替换。

后续独立包仍需受编译证明的安全schema、本应用发布资源目录、真实数据库Web会话和防漏Guard接线；平台Agent扩展与配置编辑目录不得整体带入独立包。已有RR实库验收状态和各用途Gap保持原结论。

## 本片验证

1. 单独构建 `backend/src/Metarill.Security`，证明只有Core/Data的实际编译闭包。
2. 独立 `Metarill.Security.Tests` 不引用Runtime，用固定窄资源验证跨后台角色、发布/当前资源交集、禁用动作、平台super区别与字符串长ID合同。
3. 原Agent测试项目中运行平台身份、角色授权、当前读与Guard六类离线回归；SQL传输由既有内存provider解释，不冒充MySQL验收。新增用例冻结Web/Agent认证的原查询序列，未知kind不能借扩展回退。
4. 平台API Release编译；密码生成合同从正确Runtime类型定位Agent schema，避免把已移出的PasswordHasher程序集误当平台程序集。

本阶段不连接数据库、不启动HTTP服务或浏览器、不执行RR并发验收。测试里的原ASP.NET TestServer仅证明离线产品HTTP管线合同。
