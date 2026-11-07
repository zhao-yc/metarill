# Agent 接入约定

- DI 单例：`AgentOAuthOptions`（VerificationUri由服务端可信配置提供）、`AgentOAuthService`、`AgentPublicRateLimiter`、`GeneratedCapabilityIndex`；`AgentAuthenticationService`与`AgentCapabilityCatalog`按请求Scoped。复用现有DatabaseSession/SqlRowStore/IAgentHostResources/IIdGenerator/AuthenticationService及Scoped IRuntimeAuthorization。
- 统一异常处理在外，`UseRouting()`之后使用`UseMiddleware<AgentAuthenticationMiddleware>()`替代原混合Bearer/web认证段。中间件同时填充AuthenticatedIdentity（完整保留菜单/按钮权限）和AgentRequestIdentity。网页登录只读loginStateId；Agent只读Bearer，不能互换。
- Controllers由既有AddControllers发现，不手写重复端点。OAuth公开POST form返回标准snake_case，不使用R；人工确认GET device、POST approve仅真实web管理员；me/capabilities/skill要求Agent授权。
- 所有Agent写业务请求必须再经过任务保护中间件；首提交只有OAuth和目录，不能单独以此放开Agent任意业务入口。目录只包含当前项目真实注册的生成MVC路由，不包含尚未实现按钮/DDL。
- Client固定metarill-agent-cli。设备长码、访问/刷新令牌只存SHA-256；刷新消费摘要在sys_session以agent_refresh_used保留用于重放家族撤销，不能作为登录会话。原授权到期不因刷新延长。
- pending设备尚未选择项目；网页显示当前已认证项目，人工批准时把账号、当前项目与客户端冻结；Agent后续默认项目来自其sys_session，不接受客户端切换项目。
- 验收环境变量METARILL_TEST_SETTINGS及METARILL_PLATFORM_METADATA；真实测试只允许metarill_test，未提供配置显式skip。测试建立唯一虚构账号/角色并按主键清理，不接触生产数据。
- 第二阶段必须同时注册单例`AgentTaskOptions`与`AgentTaskStore`，并在严格认证后、MVC执行前使用`UseMiddleware<AgentTaskMiddleware>()`。生成页写入口要求X-Yzp-Agent-Confirm=true、X-Yzp-Agent-Capability和UUID请求编号；读取也要求能力标识与真实MVC端点一致。目录外API默认拒绝，现有MCP通道委托宿主工具权限，MCP写工具必须继续拒绝GrantId身份。
- 任务使用平台库sys_agent_task的(account_id,project_id,request_id)唯一约束，正文仅保留长度前缀SHA-256；重复请求返回409并指引query_task，不重放结果。业务和账本跨库不是分布式事务，业务完成但响应/账本失败时返回unknown，必须回读；任务超过5分钟或24小时过期也按unknown处理。首期保留过期编号账本防旧编号重用，尚未提供归档删除作业。
- 请求默认1MiB、写响应缓冲4MiB。成功只有HTTP 2xx且R.code=200；业务失败/异常均不宣称not_applied。任务查询只限同账号同项目，客户端断开后账本完成使用独立取消令牌。


## 共享宿主边界

代码位于独立 `Metarill.Agent` 工程，仅引用 Core、Data、Security 与 ASP.NET Core，不引用平台 Runtime、API、生成器或设计器。平台 `MetadataRegistry` 实现 `IAgentHostResources`，既有控制器路径、设备协议、任务头及平台管理员批准规则保持；MVC显式注册共享控制器程序集，生成业务控制器仍来自各自宿主。

`AgentHostCatalog` 冻结签发项目、发布后台和实际能力所需的最小页面快照。平台使用 `ForPlatform`；独立完整应用从已核编译目录使用 `ForApplication`，超级角色只在该应用已发布后台内批准，续签继续核对真实身份域。调用者修改源元数据或返回列表不改变快照，重复页面ID直接拒绝。

普通会话与当前锁读证明由 `AgentSecuritySessionPolicy`、`AgentSecurityCurrentSessionPolicy` 提供。平台原适配类型保留并委托相同实现；共享安全层仍不读取OAuth表或接收原始授权字典。原JSON合同下沉Core并保留命名空间和公共类型查找转发，宿主源码须配套重编译，不承诺旧构造签名的二进制兼容。

完整包现已复制此工程、固定两张Agent表及原授权页面，启用独立装配模块和原中间件；普通前端片段包仍默认关闭Agent入口。实际源码包构建、三模式HTTP边界及Web导出已验证，真实CLI/OAuth/MySQL业务尚待专属租约验收；完整边界见 `docs/standalone-agent.md`。
