# 原样板三个 Agent 业务按钮的实际缺口

2026-10-28，C18。等待B98独占库授权期间只读对照原Java与当前C#代码；没有改动已固定的95bdfdc产品包、9563e79执行器或本轮租约。

## 已确认的原行为

固定Java `b6d64fb` 的样板显式声明三个手写业务动作，标准查询/CRUD继续走正式生成代码：

| 能力ID/按钮 | 原HTTP动作 | 原业务语义 |
| --- | --- | --- |
| sample-contract-preview / 86000 | POST /admin/custom/sample-contract-agent/preview | 按当前账号记录范围读取合同，返回原值、拟改备注及expectedDigest，预览不写入 |
| sample-contract-complete / 86001 | POST /admin/custom/sample-contract-agent/complete | 事务内锁当前合同，复核预览摘要及“待处理”状态，调用标准写入修改备注 |
| sample-contract-rollback / 86002 | POST /admin/custom/sample-contract-agent/rollback | 先执行同一真实修改，再以负金额触发对象规则，验证前一条SQL一同回滚 |

摘要按真实记录ID、名称、金额的十进制普通表示、备注依次编码，每字段使用UTF-16字符串长度加冒号及值，再做UTF-8 SHA-256。空值取空串；金额数据库精度和Unicode长度不能随语言默认格式改变。

## 当前C#证据

`GeneratedCapabilityIndex.Entries` 只编目命名空间含 `.Generated.Controllers` 且匹配 `/api/admin/auto/{table}/{page}` 的真实MVC动作；当前没有原Java那种显式自定义业务动作声明入口。`AgentCapability.ButtonId` 已有位置，但生成目录始终填null。`AgentHostCatalog` 的发布快照只维护页面，`IRuntimeAuthorization` 也尚无精确业务按钮权限方法。

所以保留ConfigButtons元数据和补三个Controller/Business，还不能让原Agent发现、授权和调用这三项能力。C12固定示例正式通过仅覆盖生成CRUD/OAuth/CLI，不覆盖这里；B98双库正式范围也明确不包含这三项业务。

## 必须补齐的链路

1. 维护显式业务动作、真实注册方法/路由、严格DTO/schema与发布页面/按钮之间的固定绑定，按请求过滤真实权限；保持现有生成目录，未声明动作不自动暴露。
2. 对照原Java按钮权限入口后实现同一权限语义；不能在移植时凭推测增加超级身份或新的页面操作门槛。grant范围、按钮及路径/方法都须逐次核验。
3. 手写C# Controller/Business和必要Service partial，全部持久化继续调用生成Service加入外层事务。执行摘要必须在真实范围和当前锁下复核，保留预览无写入、过期拒绝和双写回滚。
4. 先完成可编译完整包、目录/权限反例、摘要跨语言样例及原标准能力回归，再用新的精确样本运行真实Web/OAuth/CLI/数据库验收；不扩用正在等待授权的B98范围。

这是一项已定位但尚未实现的能力，不能以元数据仍在、普通CRUD可调用或已有Agent226项回归来打勾。进度见[实施表](project-progress.md)。
