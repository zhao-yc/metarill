# 原样板三个 Agent 业务按钮的实际缺口

2026-10-28，C18。等待B98独占库授权期间，对照原Java与当前C#代码，并先补共享安全模块的按钮范围身份投影；没有改动已固定的95bdfdc产品包、9563e79执行器或本轮租约。

## 已确认的原行为

固定Java `b6d64fb` 的样板显式声明三个手写业务动作，标准查询/CRUD继续走正式生成代码：

| 能力ID/按钮 | 原HTTP动作 | 原业务语义 |
| --- | --- | --- |
| action:sample-contract-preview / 86000 | POST /admin/custom/sample-contract-agent/preview | 按当前账号记录范围读取合同，返回原值、拟改备注及expectedDigest，预览不写入 |
| action:sample-contract-complete / 86001 | POST /admin/custom/sample-contract-agent/complete | 事务内锁当前合同，复核预览摘要及“待处理”状态，调用标准写入修改备注 |
| action:sample-contract-rollback / 86002 | POST /admin/custom/sample-contract-agent/rollback | 先执行同一真实修改，再以负金额触发对象规则，验证前一条SQL一同回滚 |

摘要按真实记录ID、名称、金额的十进制普通表示、备注依次编码，每字段使用UTF-16字符串长度加冒号及值，再做UTF-8 SHA-256。空值取空串；金额数据库精度和Unicode长度不能随语言默认格式改变。

## 当前C#证据

`GeneratedCapabilityIndex.Entries` 只编目命名空间含 `.Generated.Controllers` 且匹配 `/api/admin/auto/{table}/{page}` 的真实MVC动作；当前没有原Java那种显式自定义业务动作声明入口。`AgentCapability.ButtonId` 已有位置，但生成目录始终填null。`AgentHostCatalog` 的发布快照只维护页面，`IRuntimeAuthorization` 也尚无精确业务按钮权限方法。

所以保留ConfigButtons元数据和补三个Controller/Business，还不能让原Agent发现、授权和调用这三项能力。C12固定示例正式通过仅覆盖生成CRUD/OAuth/CLI，不覆盖这里；B98双库正式范围也明确不包含这三项业务。

## 本轮已完成的身份投影子集

原 `PowerInterceptor.authorizeAgentButton` 使用实际按钮授权：只有同一按钮的普通角色授权才能解除该按钮的范围限制；其它按钮、菜单、同页query或all权限均不能代替。共享 `PrincipalRolePolicy` 现在独立保留 `ExactDataManageButtonIds`，只接受当前有效角色、未删除授权、已发布且当前仍属于同一页面的按钮。范围按钮单独存在时也捕获原账号匹配值；后台super仍保持原规则。

认证与事务锁后复核共用 `AuthenticatedIdentity.WithPermissionContext`，在主体项目/后台、super和平台身份不变的前提下刷新全部权限决策，保留原Actor、会话和grant。按钮撤权时清除旧决策及已无用途的匹配值。新增范围集合和账号值均不输出到JSON；构造参数与已有页面、文件权限合同不变。

这只是内部身份决策，尚无公共业务按钮授权入口，也没有据此执行记录过滤。不能把这一子集写成三个业务动作已经可用。

定向验证（本任务源码，2026-10-28）：

| 验证 | 结果与边界 |
| --- | --- |
| 共享安全模块 | 114通过、0跳过，含16项新增同按钮/无关授权/顺序/发布漂移/投影撤销/身份保持反例 |
| Agent模块 | 226通过、25实库项跳过 |
| API模块 | 68通过、47实库项跳过 |
| 文件模块 | 168通过、58实库项跳过 |
| 完整生成包 | 实际构建及原Agent页面/宿主管道定向1项通过、0跳过；没有真实业务库 |
| 平台/示例正式两轮生成 | 705受检文件逐字未变，含24手写C#及490前端文件；CLI严格编译通过 |

## 移植时必须保留的原规则

原能力ID含 `action:` 前缀。确认来自实际按钮的 `isNeedConfirm` / `confirmMsg`：样板preview无需确认，complete与rollback需要确认，三者均以业务action记录执行任务。当前C#中确认标志与任务台账入口耦合，接业务动作时需要分别表达，不能把无需确认的preview排除在任务台账之外。

原 `VisibleWhenWritable` 只对显式声明该属性的动作增加页面更新能力及只读条件检查；普通业务按钮不能被移植时统一附加page query/update权限。原记录范围来自实际生成查询的专用入口，必须在真实动作/记录执行前校验，不能用前端隐藏按钮或普通详情读取代替。

## 必须补齐的链路

1. 维护显式业务动作、真实注册方法/路由、严格DTO/schema与发布页面/按钮之间的固定绑定，按请求过滤真实权限；保持现有生成目录，未声明动作不自动暴露。
2. 对照原Java按钮权限入口后实现同一权限语义；不能在移植时凭推测增加超级身份或新的页面操作门槛。grant范围、按钮及路径/方法都须逐次核验。
3. 手写C# Controller/Business和必要Service partial，全部持久化继续调用生成Service加入外层事务。执行摘要必须在真实范围和当前锁下复核，保留预览无写入、过期拒绝和双写回滚。
4. 先完成可编译完整包、目录/权限反例、摘要跨语言样例及原标准能力回归，再用新的精确样本运行真实Web/OAuth/CLI/数据库验收；不扩用正在等待授权的B98范围。

C18目前仅完成按钮范围身份投影，显式动作、记录过滤与三个手写业务仍待实现；不能以元数据仍在、普通CRUD可调用或已有Agent226项回归来打勾。进度见[实施表](project-progress.md)。
