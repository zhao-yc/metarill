# 分端运行时任务的固定来源与当前复核

2026-10-31，C68，起点4e012de。该切片实现服务端任务来源隔离，未完成批量HTTP、逐项目Web认证、Node v2应用或原按钮汇总。

## 已实现

- 新的内部EnqueueRuntime仅接受v2单个sync-runtime、与设备启用端完全一致的范围，以及服务器实际读取的当前设备配置和固定关联摘要。普通Enqueue仅v1，不能把分端请求放进无来源任务。当前没有新增可让HTTP客户端调用该入队方法的路由，后续协调器仍须先完成逐项目真实Web/grant授权。
- v2任务把完整DeviceScope及AssociationSha256固定进持久记录；task/group明确schema2，work/ticket携带同一来源。旧v1可选字段为null时不序列化；原核心绑定、旧v1工作/组记录和旧ticket字段顺序摘要保持。
- 领取、重复批准和紧邻应用的verify-ticket均要求本次实际配置。原GenerationAssistantController在真实Agent认证/管理员检查之后，从原持久任务取设备ID，再用当前实际Actor查库；没有从HTTP接受Scope/proof，没有克隆Actor或替换ProjectId。
- Queue再次持文件锁后重读当前设备关联，比较精确请求ID、当前Scope、原关联CAS摘要、binding/grant/本机策略；设备重置、改路径/开关/配置、同配置改绑都拒绝旧任务。领取竞争改变下一项时，旧请求ID证明不能用于新项。v1仍在原一次队列遍历中取得工作，只有v2才先读来源、查库后再次持锁领取，查库前不占用lease；未做性能基准。
- 分端任务不进入原按动作聚合的approvals目录；批准严格只包含原单任务，不能与另一个v2或普通v1确认合并。完整来源同时进入ticket与组校验，不因设备变化删除历史任务/结果；原ticket下的真实旧结果仍可回传留证，不能把历史回执用于新应用。
- 持久读取验证版本、范围、身份及摘要；重算外层校验和也不能把有来源v2改成普通v1、移除来源或扩大端范围。

设备/配置当前事实由实际读取器提供；队列本身不是认证系统，直接传合成Scope的单元测试不能证明SQL归属/OAuth。元数据读取器的受控ADO验证见[C67](device-assistant-discovery.md)。本次仅在原Claim/Authorize/VerifyTicket增加限制，没有新增控制动作或把设备Bind/Lookup/Configuration塞进旧动作。C65被自动审核拒绝的三个中间件豁免仍未修改，不是绕过该拒绝。

## 验证与失败记录

| 范围 | 结果与限制 |
| --- | --- |
| 新来源专项 | 29项，真实独占队列文件；含分端入队/重复/重启、范围/当前设备/配置/关联拒绝、领取精确请求ID、v1确认目录隔离、不合并确认、短ticket重复重验、同配置换绑及旧结果留证、重算校验和篡改、客户端proof拒绝、v1字段/旧摘要、v2查库前不领取。身份/Scope为明确合成，未运行本机实际Apply。 |
| 最终完整API | 严格149通过、49条件跳过、0失败，40秒；实际宿主/旧助手/新增来源校验同工程编译运行。真实MySQL条件未配置，不算正式登录/OAuth。 |
| 最终完整Agent | 严格444通过、25条件跳过、0失败，36秒；原认证/能力/任务及真实宿主回归，没有放行待批设备动作。 |
| 同任务元数据 | 严格551通过、25条件跳过、0失败，9秒；原读取器及C67发现未变。 |
| 产物/原界面 | 原前端、组件、模板、backend/generated及AgentTaskMiddleware差异均为空。无DDL、数据或真实工程写入，无部署。 |

首次严格构建因三个nullable/目标类型new返回未显式指定Locked泛型而失败，修正为明确泛型后继续。首轮70个助手用例为68通过/2失败：含数组的record重启回读按数组引用比较，数据及摘要相同；改为比较完整协议字节，不调整产品规则。随后补同配置改绑、HTTP模型不能注入proof及v1一次领取路径；以上最终API149/Agent444均正常收口，原日志保留。没有把早期失败、条件跳过或运行中记为通过。

使用项目固定DOTNET_HOST_PATH及严格命令：`dotnet test backend/tests/Metarill.Api.Tests/Metarill.Api.Tests.csproj -c Release --nologo --disable-build-servers --verbosity quiet -warnaserror`，并对应执行Metarill.Agent.Tests和Metarill.Metadata.Tests，附TRX。私有证据位于E:\git\metarill\.local：runtime-origin-build-first.log、runtime-origin-first.log、runtime-origin-api-closed.log、runtime-origin-agent-closed.log、runtime-origin-metadata-final.log及TestResults相应TRX，不提交产物/日志。

## 升级与未完成边界

无需DDL、数据回填或原前端再生成；API及相关程序集须配套重编译。已有v1记录无需改写。新schema2任务/组不能交由旧服务继续处理；回退前须停止新分端生产者/轮询，保留原UUID/日志和现场，不能删除任务后自动重排。尚未部署或正式升级任何环境。

Node当前仍拒绝v2工作，没有启用尚未安全接线的应用分支。后续必须匹配本机已绑定设备意图与固定来源，沿原准备得到的v2计划/范围应用及恢复，不能重新执行不带范围的旧preview或合并确认。还须批量固定意图/租约推进、逐项目实际Web会话及grant证明、原按钮结果汇总与真实MySQL/OAuth/CLI验收。C65三个设备控制动作的精确授权请求保持待答，不能以本轮服务端专项冒充真实链路已接通。
