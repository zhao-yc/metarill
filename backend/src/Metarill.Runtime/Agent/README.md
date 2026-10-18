# Agent 接入约定

- DI 单例：`AgentOAuthOptions`（VerificationUri由服务端可信配置提供）、`AgentOAuthService`、`AgentAuthenticationService`、`AgentPublicRateLimiter`、`GeneratedCapabilityIndex`；`AgentCapabilityCatalog`按请求Scoped。复用现有DatabaseSession/SqlRowStore/MetadataRegistry/IIdGenerator/AuthenticationService及Scoped IRuntimeAuthorization。
- 统一异常处理在外，`UseRouting()`之后使用`UseMiddleware<AgentAuthenticationMiddleware>()`替代原混合Bearer/web认证段。中间件同时填充AuthenticatedIdentity（完整保留菜单/按钮权限）和AgentRequestIdentity。网页登录只读loginStateId；Agent只读Bearer，不能互换。
- Controllers由既有AddControllers发现，不手写重复端点。OAuth公开POST form返回标准snake_case，不使用R；人工确认GET device、POST approve仅真实web管理员；me/capabilities/skill要求Agent授权。
- 所有Agent写业务请求必须再经过任务保护中间件；首提交只有OAuth和目录，不能单独以此放开Agent任意业务入口。目录只包含当前项目真实注册的生成MVC路由，不包含尚未实现按钮/DDL。
- Client固定metarill-agent-cli。设备长码、访问/刷新令牌只存SHA-256；刷新消费摘要在sys_session以agent_refresh_used保留用于重放家族撤销，不能作为登录会话。原授权到期不因刷新延长。
- pending设备尚未选择项目；网页显示当前已认证项目，人工批准时把账号、当前项目与客户端冻结；Agent后续默认项目来自其sys_session，不接受客户端切换项目。
- 验收环境变量METARILL_TEST_SETTINGS及METARILL_PLATFORM_METADATA；真实测试只允许metarill_test，未提供配置显式skip。测试建立唯一虚构账号/角色并按主键清理，不接触生产数据。
