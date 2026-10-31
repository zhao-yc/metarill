# 在线本地生成助手

三个原按钮和确认方式保持不变：“生成项目配置”“同步运行时”“生成全部页面”分别投递固定动作。本机开发者只需先完成一次真实设备授权、明确工程目录及允许动作绑定、启动助手；日常按钮操作无需下载JSON或手动执行CLI。平台不接收本机目录、连接或命令，不执行源码apply、安装依赖、SQL/DDL、编译或服务启动。

## 本机明确绑定

助手使用 `tools/metarill-agent-cli` 现有真实OAuth设备授权与受当前用户权限保护的profile。先按该工具说明登录项目，再执行一次：

```powershell
metarill-agent helper bind --target E:\apps\MyApp --source-root E:\src\metarill --cli E:\tools\metarill\Metarill.Cli.dll --dotnet E:\tools\dotnet\dotnet.exe --work-root E:\apps\MyApp-assistant
metarill-agent helper poll
```

此默认绑定只有 `preview`，明确无法写入。若开发者希望原按钮应用到工程，绑定时必须明确选择 `--mode apply --allow-actions generate-project-config,sync-runtime,generate-all-page --confirm`。可以只允许其中一项或两项；未列入的动作没有源码写权限。该确认也仅允许对同一已批准本机事务执行原严格恢复，不能执行其他命令。

target必须是三层归属验证通过的FullApplication；namespace从目标读取。target、可信源码、独立工作区必须互不包含，路径逐层拒绝链接。固定CLI/dotnet文件摘要每次执行前复核；生成器源码必须由开发者审阅。绑定不是框架签名、版本兼容证明或生产发布许可。

绑定将API地址、真实account/project/grant、本机target/namespace、可信源码、固定CLI/dotnet、工作区、mode/allow-actions和摘要存入受保护profile。线上只注册bindingId、namespace、策略和不可逆摘要，永远不上传目录、token、连接或本机日志。原编号和策略不可改绑；更换工程/策略须使用新的明确授权profile和新binding，不能手工修改摘要放宽权限。

若本机保留阶段一缺mode/allow-actions的旧preview绑定，新Node明确提示 `helper_legacy_binding`：先停止旧助手，保留原profile、旧工作区候选/plan/journal及现场；为新绑定准备**另一个新的空work-root**，不能复用旧目录或删除证据。再用 `metarill-agent auth login --url https://实际项目/api --profile generation-v2` 重新完成真实设备授权，再给该新profile执行上述 `helper bind` 和 `helper poll --profile generation-v2`。服务器按旧字段重算原摘要，只读把旧记录映射为preview，不修改旧文件、不授予apply，也不阻断新profile注册；多绑定列表中须选择新编号。此步骤是一次升级/改变策略的明确授权，不是日常按钮操作。

注册意图先刷盘，网络响应未知时 `helper poll` 恢复原pending编号，不产生新的孤儿绑定。默认poll持续出站轮询，Ctrl+C在当前任务收尾后退出；`--once`仅处理一轮，供诊断和测试。授权失效不会自动重新登录、安装工具或启动服务。

## 平台入口与真实授权

原路径保持：

| 原按钮 | GET入口 | 唯一动作 |
| --- | --- | --- |
| 生成项目配置 | `/api/admin/custom/index/generate_project_config` | `generate-project-config` |
| 同步运行时 | `/api/admin/custom/index/sync_runtime` | `sync-runtime` |
| 生成全部页面 | `/api/admin/custom/index/generate_all_page` | `generate-all-page` |

这三个入口仅接受真实Web管理员会话的当前项目，不接受query或正文。原人工确认产生 `X-Yzp-Agent-Confirm: true`、唯一规范UUID `X-Yzp-Agent-Request-Id` 和已选 `X-Metarill-Assistant-Binding`。UUID绑定完整发布快照/动作，重复相同请求幂等、不同内容拒绝。平台BackendOnly明确拒绝，不能伪装完整ZIP或已写入。

`GET /api/admin/sys/generation-assistant/bindings`只列本账号项目且grant仍获批准的绑定，多设备时选择一次本次目标。`GET .../requests/{uuid}`只查询当前账号项目任务，不返回元数据、token或目录。首页在发请求前缓存有界任务标识；网络结果未知和重新进入首页只查询原UUID，不自动重发原GET。退出页面或账号/项目变化使迟到回执失效。

Agent专用POST控制面是 `helper/bind`、`helper/claim`、`helper/report`、`helper/approvals`、`helper/authorize`、`helper/verify-ticket`、`helper/application-report`。Middleware仅为这个准确Controller和方法集合开放控制通道，Controller再次校验真实Agent管理员及account/project/grant；不加入普通生成CRUD能力目录，也不允许Agent借Web原按钮入队。

## 快照、分组与短ticket

平台用与打包相同的发布脱敏规则生成完整元数据快照，固定schema/version/action/project、metadata/request SHA。结构中原有SQL定义仍是元数据，不代表平台或助手执行SQL。普通请求正文32KiB、发布请求4MiB、状态文件8MiB有界；未知/重复JSON键、未知参数、跨账号/项目/grant、失效绑定均拒绝。

每次点击只确认一个action。准备候选使用固定C# `update prepare`，先保留原单动作plan和 `requiredActions`。缺依赖时明确提示回到原按钮分别确认，绝不把单动作偷偷改为all。本机仅将**同binding、account/project/grant、namespace及metadata SHA**下已经分别确认的UUID合成组，动作恰好等于这些确认的并集，且必须是本机allow-actions的子集。不同快照、重复动作、已归其他组或过期的意图不能混组。

组使用同一完整候选重新执行原 `update preview --actions 精确集合`，保留新组plan，不覆盖单动作plan。本机先持久化组UUID/原请求证明/计划SHA，再申请ticket。服务器原子持久化组、原UUID/requestSHA集合、同快照和plan SHA，默认短ticket90秒且不超过原任务TTL。ticket发出前和临近本机apply前的 `verify-ticket` 都重新验证真实grant及平台当前发布metadata SHA。撤权阻止之后的控制请求；已经开始的本机文件事务按原日志收尾或恢复，不能承诺网络撤权瞬时中断某次文件替换。

本机再核对ticket、固定策略、每个原请求hash、plan/target/candidate/namespace及scope闭包，才以无shell参数数组运行固定C# `update apply --confirm 原planSHA`。C#Apply仍重新预览校验全部候选/目标/保护文件和持久日志，任何漂移拒绝。不通过HTTP接收路径、transaction、任意命令或替代CLI。

## 持久状态与恢复

服务器单节点状态固定在自己的 `.local/generation-assistant`，进程锁加文件互斥锁、同目录临时文件Flush(true)后原子替换，所有读盘重验完整快照/身份/摘要。任务默认15分钟、lease5分钟，过期终结不重派；绑定128/任务与组1000上限达到时明确拒绝，首期不自动删除审计。摘要用于漂移检测，可信服务器文件系统仍是边界，不是数字签名。多节点队列、吞吐或长时任务续租不在本轮验收范围。

预览状态为 `queued/leased/preview_ready/dependencies_required/failed/expired`，始终 `applied:false, outcome:not_applied`。已经发出的应用组在无最终回执时为 `awaiting_application/outcome:unknown`；不能因ticket过期推断从未写入，不能重新派发该组。只有独立应用回执 `applied` 才显示生成完成；恢复成功为 `rolled_back`，证据不完整或后来字节为 `manual_recovery/unknown`，保留现场。

本机任务先持久 `preparing`、原快照/plan和预览出箱。领取新任务前先重传prepared出箱，即使服务器早已保存终态且Claim为空也不漏回执。预览ACK与应用意图之间单独持久确认，重启不重新生成已经完成的候选。准备阶段中断明确失败，保留原产物。

应用先刷盘 `authorizing/authorized/applying`，成功或失败结果先刷盘 `reporting` 再上传。响应丢失只重传相同回执，不重新apply。重启遇 `applying` 先调用固定只读 `update apply-status --target 固定目标 --plan 原计划 --confirm 原SHA`；只认可本机原完整计划/journal/旧identity、全部备份/暂存和完整目标指纹。`committed`仅重报；确证原active事务才调用原recover的**本机反查ID**；第三种内容、缺日志、篡改、多匹配等均unknown，不重跑apply。没有journal不是未写入证明。

已明确本机恢复授权只回滚同一个原事务。重启时原事务恢复可以先于网络身份请求；撤权/离线时恢复结果仍留本机，不能据此开始新apply或伪造服务器已收到。普通IDE不遵守助手锁，执行期间应停止冲突写入。PID锁只回收已证明退出进程，未知PID/遗留gate保留现场需人工核查；不声称所有崩溃都能自动恢复。

本机有界stdout/stderr尾部只写私有diagnostic文件，不上传或进入通用CLI错误详情。失败候选、计划、备份、journal和出箱保留，不能用自动清理擦掉证据。

## 首次在线切片的历史验证（2026-10-19）

以下是冻结链adaa20d的历史批次数字，不能当成本次原前端/journal v3重新实测结果。该批次分别验证服务器真实OAuth/隔离Server-200库、真实本机文件/C#CLI、真实首页Vue方法和共享handler，不把这些独立证据冒充一条生产端到端验收：

- 队列及原入口边界最终26/26通过（含旧preview记录只读兼容），API/Runtime编译无警告错误；真实OAuth完整Agent18/18通过，含旧GET入队、应用前ticket跨grant/撤权拒绝，零跳过，不执行DDL。
- 前端完整236/236通过，含原三个确认、固定独立入口、只预览/依赖提示、回执丢失只查询、迟到或伪applied不显示完成。首次缺vue/xlsx属于独立克隆依赖缺失，锁定离线依赖补齐后再测。
- Node真实C#CLI最终28/28通过，零跳过；覆盖两动作分别确认后实际apply、不加第三scope，AppID/partial保持，服务端保存结果后ACK丢失重传、实际committed日志的applying切点回放、应用前401目标保持、真实原active事务恢复、第三种用户字节保留、非空工作区和不完整写策略拒绝。故障使用测试临时夹具在原Updater实际替换后注入，不把状态回放冒称操作系统进程崩溃实测。
- C#独立apply-status15项真实临时文件/CLI回归由并行切片通过，覆盖正常、补壳、页面增删、自动回滚、active、完整证据缺损/篡改、候选退役、锁/链接及真实CLI。

未运行HBuilderX、真实浏览器或真机，不宣称正式生产认证、多节点或整个平台功能完成。首次Node应用批次暴露未await导致锁提前释放的分支、真实HTTP批次发现精确方法白名单漏补，已修正并保留旧失败日志；最终批次单独记录，不叠加重复次数。

最终证据保存在本任务私有 `.local/generation-assistant-results`：`assistant-application-queue-freeze.trx`、`assistant-application-http-freeze.trx`、`node-cli-application-freeze.log`、`frontend-application-freeze.log`。此前失败/中间批次保留，不累计重复通过数。main的template与style规范化字节与切片基线相同，只改在线handler和必要的回执恢复/取消生命周期；这些测试不能证明生成页面已完整符合原YZP布局和交互，该项仍须按原前端专项验收。

## 原前端与journal v3整合（2026-10-20）

本次从原前端恢复后的 `e6fe956` 独立整合冻结在线链；只抽取发布脱敏与请求/助手能力，保留原三按钮确认、首页模板和CSS原字节。CLI仅增加 `target-identity/prepare/apply-status`，不覆盖当前发布stage保护、开发改密、原页面生成及journal v3。没有把历史离线JSON首页作为最终UI。

`apply-status`复用当前严格 `ReadJournal`：v3固定原壳create必须含持久确认索引；发布后、确认刷盘前中断，即使文件字节等于候选，也只能保留人工核查。当前active可反查同一原事务，但recover不能删除未确认的存在路径；必须先人工审查移走该路径，再显式恢复。没有active或缺失确认不能推出成功。旧v2日志只兼容无受保护原壳create的事务，不能借旧版本绕过归属确认。

本次新增真实临时Full包测试：确认后成功、候选退役、v2正常事务、v2伪原壳create拒绝、确认索引丢失，以及最后预检后出现的异/同字节第三方文件、发布确认间隙。Node用同一真实Updater在原壳发布后确认前切断，再回放本机applying状态；结果必须manual_recovery且文件/plan保留，不能重apply或向平台报告applied。这是确定性故障注入与真实CLI恢复，不冒充操作系统断电或浏览器操作实测。

本轮日志保存在独立clone的 `.local/online-original-tests`，与上述历史批次分别记录。Server-200既有隔离库只创建并按本测试随机ID清理账号/角色/grant/会话行；未DDL、改平台元数据、启动产品服务或上传部署。本轮未验证在线助手真实浏览器联动、移动端/真机或正式生产认证；服务器applied状态是受授权助手的回执，不是服务器独立验证远端文件字节的证明。

本次实际结果（不累加重跑/历史批次）：

| 验证范围 | 本轮结果与证据 |
| --- | --- |
| 本机请求、apply-status及原壳恢复 | 48个唯一用例：首批47通过、1个旧可信源夹具失败；夹具改为原公共菜单源后该1项复验通过。`assistant-local-v3.trx`和`assistant-runtime-source-final.trx`分别保留。包含新增5项v3/v2/race状态用例。 |
| Packager脱敏、自身构建及发布保护 | 5/5通过，`assistant-publication-regression.trx`；没有覆盖原发布stage保护或开发改密模板。 |
| 队列和原GET权限/参数边界 | 26/26通过，`assistant-queue.trx`；实际平台宿主三个GET拒绝及schema历史不变另1/1通过，`assistant-real-three-get-final.trx`，只退出本次客户端会话。 |
| Server-200真实身份与普通Agent回归 | 助手grant/原GET/ticket/撤权1/1，原任务Middleware权限/幂等/错误边界4/4，均零跳过。见`assistant-real-grant.trx`、`assistant-agent-task-regression.trx`。 |
| 原前端 | 全243/243通过，含首页online与DOM/CSS冻结6项（不能重复相加），`frontend-full.log`。 |
| Node工具与真实本机CLI | 通用CLI19/19及check通过；helper原9项首批通过，新v3第10项末尾误断言回执不存在字段，修正测试后单独1/1通过。见`node-helper.log`与`node-helper-v3-final.log`；不写成单次10/10。 |

初次C#编译中测试hook的init-only赋值写法已改为构造初始化；新测试曾把应用回执的固定status合同与展示view的applied字段混淆，只纠正断言，不改产品协议。可信源夹具的修正也没有放松原菜单能力门禁。全部失败及复验日志留存，未将失败日志改写成通过。

## C65设备关联准备状态（2026-10-31）

原设备与本机助手对应、目录/配置摘要及CLI一次性关联代码已补齐，但实际Agent控制通道尚未登记，自动权限审核要求明确批准，正式请求仍会被拒绝。原三个单项目动作保持，原批量按钮尚未接通。详见[实际验证与未完成](testing/device-assistant-association.md)和[精确待审阅方案](device-control-gate-decision.md)，不能将本地替身HTTP或applied=false的登记成功计为批量同步完成。

C68加入[分端任务固定来源与当前复核](testing/runtime-assistant-origin.md)：旧v1不新增null字段，v2内部任务固定设备配置/关联并逐次重验，禁止与普通确认合并。Node v2应用与批量HTTP尚未启用；设备三个专用控制动作仍待明确批准，不能把组件测试写作在线批量成功。
