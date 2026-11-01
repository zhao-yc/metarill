# 设备控制通道登记：已批准并验证源码分流

日期：2026-10-31。用户已批准保留原批量按钮接在线本地助手；本文件只列出自动权限审核额外要求确认的中间件登记，不重复询问原交互方案。

## 实际问题

C65已接设备配置预检、关联和状态回读的C#控制器及Node CLI。原AgentTaskMiddleware仅明确放行原GenerationAssistantController的Bind/Claim/Report等控制动作。新GenerationAssistantDeviceController未登记，所以真实Agent会进入普通业务能力目录分支并被拒绝；匿名路由、直接控制器测试和替身HTTP不能证明该链路可用。

独立探针使用当前实际Agent程序集、实际新控制器的MVC元数据和明确合成身份，复现“被普通能力目录分支拒绝，未到控制器”，返回非零。没有绕过实际OAuth认证的正式验收，也没有连接数据库。原中间件尚未修改。

## 用户已批准的精确改动

只在原专用助手控制通道旁，登记以下实际控制器/动作；不按URL前缀放行，不登记其他方法或控制器：

| 实际类型 | 动作 | HTTP路径 | 作用 |
| --- | --- | --- | --- |
| Metarill.Api.Controllers.GenerationAssistantDeviceController | Bind | POST /api/admin/sys/generation-assistant/helper/bind-device | 创建或按旧摘要明确替换本账号/设备/项目关联；不生成/应用源码 |
| 同上 | Lookup | POST /api/admin/sys/generation-assistant/helper/device-binding | 只读回查关联和旧摘要，配置变化明确标记失效 |
| 同上 | Configuration | POST /api/admin/sys/generation-assistant/helper/device-configuration | 只读实际设备/配置摘要与启用端，不返回目录或凭据 |

前置AgentAuthenticationMiddleware仍逐请求验证真实Agent会话、grant、账号与项目；控制器仍要求平台管理员且与真实Agent身份一致，拒绝平台BackendOnly及query。设备读取器实际校验活动设备拥有者与当前项目配置。Bind还校验本机核心绑定的账号/项目/grant、apply模式、允许sync-runtime、启用端路径摘要、预检配置摘要和明确的旧关联摘要CAS。原核心v1绑定、普通业务能力规则及源码写入口不变。

**影响与风险：** 这三个设备控制动作将与现有助手Bind/Claim等一样，不经普通业务能力目录和sys_agent_task业务账本；关联只写助手专用状态文件，受目录、身份、配置摘要和CAS约束。不会由此次登记获得业务CRUD、SQL、DDL、任意路径或源码应用权限，但专用控制通道的动作集合确实增加三项，不能把它描述成完全没有安全边界变化。

具体未应用补丁位于E:\git\metarill\.local\device-control-gate-proposal-20261031\agent-device-control.patch；SHA-256：85973100fd0f830721969901654c9b10abe6a87a5716e50e0f1cb7add64e9258。该文件为原审阅文本；2026-11-01收到用户对这三个精确动作的直接批准后，已应用同摘要两行改动，未生产部署。

## 自动审核结果与备选（批准前历史）

自动权限审核拒绝了上述持久修改，理由为：会跳过能力目录和账本校验，用户未明确授权这种豁免，存在扩大权限与审计缺失风险。源码保持未登记状态，不能改名塞入旧Bind或通过其他工具间接绕过。

已经只读核对继续走业务能力目录的备选：现目录仅编译真实生成CRUD与元数据固定业务按钮；把设备控制额外发布进去需要另设管理能力声明、权限过滤及固定写UUID/账本接线，还会改变既定“助手控制不进入普通业务目录”的合同。这不是简单修复，也不能未经确认换一套权限方案。

建议批准本文件列出的三个精确控制动作沿原助手模型登记，并配套正向真实方法/未知动作及其他控制器的负向中间件测试；不批准则保留当前拦截。正式MySQL/OAuth、批量队列和原按钮汇总仍须分别完成，不能用本次批准或合成探针代替。


## 2026-11-01 已批准登记及实际验证

用户明确批准Bind／Lookup／Configuration三精确动作，原补丁先经git apply --check再应用；仅增加真实控制器全名与动作名匹配的两行，没有按URL前缀扩大通道。前置Agent认证、grant有效性，以及控制器管理员／设备归属／摘要／CAS仍保留。原业务能力目录、写确认、UUID及sys_agent_task账本分支未改；这三控制动作依批准沿助手专用通道不进入业务目录和账本，原风险说明仍适用。

先用真实ControllerActionDescriptor／实际MethodInfo复现3正例失败、4阴性通过；应用后与控制器身份边界共14通过。未知动作、大小写变化、同方法名其他类型、缺MVC描述符即使使用同URL也进入原业务目录，不得到专用通道。合成身份只证明中间件分流；完整API222通过／49条件跳过、Agent444通过／25条件跳过，严格构建零警告错误。原776文件逐字保持，没有SQL、OAuth或本机工程应用，不计真实设备／批量闭环通过。

证据位于忽略目录 `.local/device-control-approved-20261101/` 的red.log、green.log、api-all.log、agent-all.log、results及protected-proof.json；批准前的拒绝和未登记探针保留。旧完整包需使用当前可信源经正规运行时更新获得新Agent代码，保护手写部分；无DDL或元数据迁移。生产部署、真实Web／OAuth／关联／Node领取／本机应用／终态汇总仍分别验收，不因源码登记即宣布已部署或真实可用。
