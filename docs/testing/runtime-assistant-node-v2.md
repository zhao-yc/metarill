# Node 助手 v2 分端计划应用

2026-10-31，C69，起点51b920e。该切片接通本机 Node 助手对服务端 v2 分端任务的准备计划应用与恢复；没有启用批量 HTTP、真实 OAuth 或设备控制中间件豁免。

## 应用边界

- v2 任务必须带服务端固定的 `runtimeOrigin`，包含设备、项目、配置、设备/配置摘要、启用端和关联 CAS 摘要。Node 还必须在本机 `device-binding.json` 中找到同设备、同摘要、同关联的 `bound` 意图，并重新计算目标后端/前端路径摘要；不自动换绑或采纳新配置。
- v1 任务保留旧工作目录、旧 `application-plan.json`、旧多动作确认和旧字段形状；v2 应用只读取原准备阶段写入的 `plan.json`，不调用无范围的全量 preview，也不请求或合并其他 action 的 approvals。
- v2 应用确认集合严格为当前单个 `sync-runtime` 任务，ticket、应用状态和本机回执都携带同一来源。未选端源文件、两层归属记录、管理来源声明和手写扩展都由 C# 更新器保护；Node 不自己重算或替换来源清单。
- 应用前重新核对设备意图和 ticket；设备配置、路径、关联、绑定策略或真实身份变化会在准备/应用前拒绝。已开始的原事务只使用固定计划和 C# CLI 状态恢复，不能重新 preview 或重跑 apply。
- 应用结果仍明确区分 `applied`、`rolled_back` 和 `unknown`；网络回执丢失只重传同一结果。设备关联注册仍只报告 `applied:false`，不会把关联成功说成源码已应用。

## 验证

| 范围 | 结果与限制 |
| --- | --- |
| 协议与范围专项 | 37项通过，覆盖v1/v2快照互斥、来源字段、单/双端、计划降级/扩展/动作注入、未知字段和路径摘要。 |
| v2真实固定CLI专项 | 8项通过：普通FullApplication后端/前端/双端，管理FullManagementApplication后端/前端/双端，回执未知沿原plan恢复，设备意图失配拒绝且不写目标。实际调用固定C# CLI，目标为独占临时包。 |
| Node完整回归 | 129项通过、0失败/跳过，持续571738毫秒；包含原CLI、v1生成/事务恢复、设备路径/意图、v2范围和真实固定CLI用例。 |
| 前端/模板/生成及中间件 | 原前端、组件、模板、`backend/generated` 和 `AgentTaskMiddleware` 差异为空；没有真实工程、MySQL、OAuth、MCP 或设备远端写入。 |

完整回归第一次因整体运行超过观察窗口而未有最终摘要，之后按同一命令正常收口；期间没有改源码或重启同一测试。所有用例使用独占临时目录；HTTP 是明确的协议替身，不能替代真实 Agent grant 或 MySQL 验收。

命令：`npm --prefix tools/metarill-agent-cli test`；语法检查为 `npm --prefix tools/metarill-agent-cli run check`。证据：`E:\git\metarill\.local\runtime-helper-full-closed.log`、`runtime-helper-v2-targeted.log`及相应临时测试输出，不提交日志/构建产物。

## 未完成边界

原批量按钮仍未接通服务端批量任务；还须逐项目真实 Web 会话/grant、设备专用控制通道授权、批量固定意图/租约、结果逐项汇总和正式 MySQL/OAuth/CLI 验收。C69 只证明本机 v2 计划应用可以沿原范围工作，不能把合成身份或独占临时包写作正式跨项目验收。
