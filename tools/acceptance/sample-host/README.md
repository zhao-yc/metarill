# 原 V8 三个 Agent 业务动作的 C# 手写样板

本目录对应固定 Java `b6d64fb2ad4ac25b004eeaa326092de3cfb7f311` 的 `SampleContractAgentController` / `SampleContractAgentBusiness`。只面向 `backend/tests/Metarill.Generation.Tests/Fixtures/v8-global-cached-sample.json` 的原三表、三页、三按钮验收，不会随普通项目生成器自动下发。

## 安装边界

先用原完整元数据生成命名空间为 `Metarill.GlobalSample` 的**全新 FullApplication 包**。不要把这些固定页ID/按钮ID用于其它业务目录。

| 手写源 | 新验收包中的位置 |
| --- | --- |
| SampleContractAgentBusiness.cs | backend/Application.Host/ |
| SampleContractAgentController.cs | backend/Application.Host/ |
| SampleContractDigest.cs | backend/Application.Host/ |
| SampleContractButtonHandler.cs | backend/Application.Host/ |
| SampleHostCustomization.cs | backend/Application.Host/ |
| SampleContractService.cs | 合并到 backend/generated/Services/SampleContractService.cs 的手写partial |

`SampleHostCustomization` 通过原宿主的 partial 钩子注册 Business 和原三按钮处理器，不修改受管 `Program.cs`。已有钩子实现时需要合并注册，不能添加第二个实现或覆盖既有手写业务。金额规则属于对象 Service partial；不能移到 Controller，也不能写 Mapper 或绕开生成更新。

Java 对照文件只用于摘要验收，不放入应用宿主。执行时需要固定原样板编译类及其依赖的完整类路径；它直接反射原方法，不再复制摘要算法，也不启动 Spring、数据库或容器。固定向量包含中文、非BMP/组合字符、空值、金额scale及大整数ID。

## 动作

- `action:sample-contract-preview` / `86000`：只读取该按钮当前可访问的合同，返回拟改备注和摘要。
- `action:sample-contract-complete` / `86001`：外层事务中按按钮范围读/锁实体，重算摘要并检查“待处理”，调用共同生成更新核心。
- `action:sample-contract-rollback` / `86002`：先做同一真实生成更新，再提交负金额触发原对象规则，验证外层事务撤销第一笔更新。此动作只属于验收样板。

显式Agent动作HTTP正文仅接受 `id` 和 `expectedDigest`，不接受项目、主体、按钮、回滚开关或任意更新字段。确认来自固定原按钮配置，preview无需确认但真实Agent调用仍登记任务。显式版本冲突保留 `YZP_CONFLICT` / HTTP409，同时任务结果保持 `unknown`，禁止自动重试；任务记录失败仍回退503/unknown。

## 当前验收

2026-10-28：完整包严格编译、两轮再生成保护五份手写源、真实MVC发现、三个业务流程及对象规则通过；数据库传输与权限结论在这些业务用例中仍为离线替身。真实完整Web宿主已验证三个接口匿名401。已核原JAR与实际Business/Entity类字节，并在Java 21.0.12直接调用原私有digest方法；10组摘要与C#三种文化设置的固定向量一致。

尚未完成真实MySQL锁/回滚、事务锁后的当前角色/范围证明、文件引用事务、OAuth/CLI业务闭环及跨业务库组合。原详情页仍走 `/admin/sys/config_button/execute`；本目录的显式Agent动作不代表普通独立应用的Web中央按钮执行入口已接通，页面和交互不因此改变。详情见[专项记录](../../../docs/sample-contract-actions.md)。

B98已冻结的95bdfdc产品包及bd79df3执行器不包含这些新业务，不覆盖本目录后续正式验收。不能修改旧包或复用已结束租约；正式运行前另固定完整产物及精确样本范围。


## 原中央入口接线（2026-10-29）

更新后的 FullApplication 携带原 `/api/admin/sys/config_button/execute`。显式安装新增手写 Handler 后，三个原页面按钮复用同一 Business/生成 Service；详情原值按发布金额 Scale 转成已有摘要，过期仍拒绝，禁止以服务器最新值补齐摘要。原页面与确认/刷新交互不变，Agent严格请求和任务规则不变。新安装共六份 C# 手写源，已有 partial 注册须合并。

[原中央入口专项](../../../docs/original-business-button-execution.md)区分了真实模板/MVC回环与正式登录/实库验收。父范围、分布式执行互斥、跨库及正式Web/OAuth/CLI仍待，不能把当前子集当作全部业务按钮验收完成。
