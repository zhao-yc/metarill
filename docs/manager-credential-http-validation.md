# 原账号表单凭据 HTTP 回归

本切片只准备测试源码，基于已提交 `86c57ced`，不读取根工作区尚未提交的正式生成物，不修改产品、前端、角色规则或默认账号。运行前必须从已提交元数据和生成器正式再生平台；用例先检查实际 DTO 含 Password，不满足即在创建数据库夹具前明确失败，不能自动跳过冒通过。

三个真实 MySQL 用例复用 RoleSecurityFixture 的 TestServer、真实 GeneratedRegistration、AddRealmWriteCoordination、当前认证和 YzManagerService 手写 partial。操作者为普通平台账号，只授予原账号页 query/new/update；不增加超级身份，不替换哈希或授权。仅为原 IdentityController 在测试宿主补菜单依赖和 login 限流政策，原旧测试默认路径保持不变。

1. 原完整实体新增：新口令、旧 salt/oldPassword 空回显、字符串角色 ID 和 wire“启用”1；验证物理 disabled0、PBKDF2 验证成功、详情/写回执固定八星/空旧字段、列表不含凭据。原掩码整实体更新姓名时摘要字节与省略角色原编码不变。然后经过产品原表单 login/exit_login，真实 session 逻辑删除，最后精确清理新账号及所有会话。
2. 原新增缺口令、null、空值、掩码、预编码摘要均明确拒绝，随机账号不存在。
3. 原 HTTP 更新非法密码或非空旧字段，以及直接 Service 的掩码/null/预编码请求，逐项以数据库全行 SHA256 指纹证明无写入。指纹与布尔断言不打印原口令、摘要或整行。

账号名称在请求前生成随机 nonce 并确认不存在；清理前必须重新核对 id、name、insert_manager_id 和 yz_project_id，再删除该账号自己的 sys_session 和账号，核实含墓碑零残留。无法证明所有权即停止并保留异常。操作者的角色/授权/会话沿原 RoleSecurityFixture 精确主键清理，不触及默认 test-admin、原平台元数据或37图。测试不生成或备份任何密钥/口令文件。

本轮仅源码/离线编译准备；没有连接 Server200，没有执行这三项真实 MySQL 用例，没有浏览器或人工口令输入验收。缺少明确私有测试配置时按原 AgentIntegrationFact 自动跳过；不得把 skip、编译或 TestServer 设计记为实库通过。待根分配独占数据库窗口后运行本类；该类与安全 RR 组共享非并行集合。
