# 完整应用正式 Web 协调注册

本片仅补齐 `FullApplication` 正式 Web 宿主的依赖注入闭包，不开放安全管理页、角色工作台 HTTP、Agent 或新的写入能力。

## 注册与身份

`WebSecurityCoordination.cs.tpl` 是唯一新增注册源头。`Program.cs.tpl` 仅在非 Development 分支调用；显式开发模式仍沿用原 `DevelopmentAuthorization` 普通业务辅助路径。注册函数本身也拒绝 Development，不能把内存账号 ID 作为持久身份。

正式分支将 `ApplicationWebAuthorization` 注册为 Scoped 具体实例，`IRuntimeAuthorization` 与 `ISecurityIdentityAccessor` 都指向同一实例。实际身份仍来自当前请求已经认证的 `HttpContext.Items[AuthenticatedIdentity]`，不从请求项目、后台或单独的 `RuntimeActor` 建立安全身份。

`ApplicationSecurityCurrentSessionPolicy`、`SecurityRealmCurrentReader`、`SecurityRealmMutationGuard` 与 `RolePermissionBusiness` 均为 Scoped。`IRecordMutationGuard` 是同一 `SecurityRealmMutationGuard` 的别名。没有平台 `ConfigurationMutationGuard`，没有发布目录回退；既有固定目录两个接口继续共用同一个 Scoped 适配器，所有协调组件消费宿主原有的同一个 `SqlRowStore` / `DatabaseSession`。必需适配器缺失时解析失败。

本片不改变共享 Reader / Guard / Business 的公共合同、锁序或授权规则。当前会话政策仍明确拒绝 Agent。固定目录只证明不可变资源，不代替真实账号、角色、权限、会话行的事务当前读。

## 打包与已有项目跟进

- 新 Full 包固定包含 `SessionPolicy.cs` 和 `WebSecurityCoordination.cs`，并由实际 Host 项目编译；BackendOnly 不包含这两个 Host 文件，维持原开发宿主边界。
- 两个新文件属于 `sync-runtime` 固定受管名单。旧 Full 可以读取归属并预览，必须显式执行 `sync-runtime` 才补齐新文件和 Program 注册。仅配置更新会报告缺少 Runtime，不会悄悄补入。
- 当前候选必须具备两项文件及归属，不能同时删文件、删清单来伪称完整包。目录 JSON 与编译 Binding 继续属于配置生成，本片不把日常 Config 更新永久绑定为 Runtime 更新。
- 源码更新不执行结构同步、初始化、依赖安装或进程启动。保留手写 Host、Service partial、私有 appsettings 和 AppID。新增文件写入后的故障继续由原持久 journal / 备份严格回滚。

## 验证与边界

新增回归先由真实完整包编译，再直接执行包内注册函数及原协调组件；离线 `HttpContext` 仅注入测试身份，ADO 只替代连接与事务。验证同 Scoped 身份/守卫、必需适配器失败、目录对象证明、当前及已结束事务、开发身份拒绝，并测试旧包显式更新、候选缺失与失败回滚。

首轮保留 14 项的 13 过 / 1 失败记录：失败是新测试误预期守卫现有冲突消息含“准备”；同时修复本轮新增的 xUnit 集合大小分析器告警。产品未为断言修改。最终冻结邻近回归 `-warnaserror` 为 **114/114 通过、零失败、零跳过**：实际包认证/固定目录/协调注册 82 项、包 profile/更新保护 21 项、当前会话政策 11 项。首轮与终轮重叠，不累计成 128 项。

终轮报告位于独立开发 clone 的 `.local/web-security-coordination/web-coordination-neighbor-v2.trx` / 同名 `.log`；TRX SHA256 为 `46d25b9a3e1ada03c47cff490082cf48d467846fd034698538c9040368d0df55`。本轮没有宣称全量 LocalGenerationUpdate 类通过：基线的两个手工能力候选夹具尚待主线单独合入已审 Binding 同步适配。

本片没有执行 HTTP、MySQL、RR 并发、浏览器或设备验收。RuntimeA、安全 Service 三个手写 partial、5 个平台 Custom 前端依赖与未知声明门禁继续保留，不能将注册完成称作完整安全运行时已经下发。
