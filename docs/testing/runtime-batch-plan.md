# 批量运行时：计划、入队及原协议边界

当前状态（2026-10-31，C78）：C77已纠正原sysParams设备及入队假成功；C78增加服务端持久批次和只读状态查询。原按钮的客户端固定批次、等待/逐项汇总与网络未知恢复尚未接入，真实Web/OAuth/MySQL仍未执行。以下各切片的离线证据不能代替完整批量同步验收。

2026-10-31，C70，起点a3e9843。该切片为原“同步当前设备全部项目运行时”按钮提供只读计划入口，保留原页面操作，不创建任务、不应用源码。

## 计划合同

- `POST /api/admin/sys/generation-assistant/runtime-batch/plan` 只接受严格的 `{deviceId}`，通过现有网页登录会话和平台管理员检查，再调用设备全部项目发现。
- 每个项目独立返回 `skipped`、`failed` 或 `needs-project-authorization`。未启用、启用端缺路径、未配置、坏配置和不存在项目继续保留原明确错误；配置可用只表示待目标项目真实授权，`CanCreateTasks` 固定为 false。
- 结果不含本机路径、设备令牌、命令或异常正文。计划层不使用当前工作区项目代替全部项目，也不复制 Actor、改 ProjectId、创建队列任务或生成 v2 ticket。
- 目标项目的真实 Web 会话/grant、设备关联、配置摘要和本机意图必须在后续逐项协调时再次核验；网络未知只回读原项目结果，不能把计划返回当应用成功。

## 验证

4项专项通过、0失败/跳过：配置可用转待授权，跳过/失败结果逐项保留，缺Scope不创建任务，计划不接收客户端Actor且不泄露路径/令牌。测试使用合成发现结果，未连接数据库、OAuth或本机助手。

C67发现与C68服务端来源校验、C69 Node v2原计划应用分别提供后续边界；本入口尚未接通原页面的网络回调和逐项目授权入队，不能写作批量同步完成。

## 授权预检增量

C71在同一控制器增加 `POST /api/admin/sys/generation-assistant/runtime-batch/preflight`。它先重新发现全部项目，再对每个可用项目使用同一账号的真实网页登录会话重新认证目标项目；目标项目身份必须是管理员且账号不变。服务端随后只回读该项目已有绑定，并逐个调用批准grant检查，返回 `ready-for-enqueue`、`needs-agent-binding`、`target-project-reauthentication-required`、跳过或失败。

预检不创建队列任务、v2 ticket或源码计划，不返回grant、令牌、本机路径或异常正文。当前项目复用已认证身份，其他项目必须提供原认证头对应的Web会话；选择字段不会切换Actor。严格编译通过，真实OAuth/MySQL未执行。

## 确认后的逐项目入队

C72增加 `POST /api/admin/sys/generation-assistant/runtime-batch/enqueue`。请求必须带原按钮的一次确认头和批次UUID；服务器重新发现配置、重新认证每个目标项目，重新读取当前设备配置，检查同项目当前关联、批准grant和绑定，再创建单个v2 `sync-runtime` 任务。

项目任务UUID由批次UUID和规范项目ID通过固定摘要生成，同一批次重复点击回到原任务；不同项目不会共享任务。任务来源固定当前DeviceScope和关联摘要，服务端不接受客户端路径/摘要。项目失败只影响该项目，取消和基础设施异常继续抛出；回执只返回任务编号和稳定状态。

本层仅完成入队，不能代表本机助手已领取、准备或应用；Node v2和C68/C69的来源/ticket校验仍是后续执行门槛。当前 API 严格编译通过，专项5项通过；真实OAuth、MySQL和原页面网络接线仍未验收。

## 原页面旧路由兼容

C74增加后端兼容路由 `POST /api/admin/custom/index/sync_runtime_all_projects?parentId=...`，与原项目列表页面保持同一 URL、POST、parentId 和一次确认交互。页面、按钮、样式、`customButtonHandler` 和刷新流程没有改动；服务器把旧请求转换为逐项目 v2 入队，仍重新查设备拥有权、项目配置、目标项目 Web 身份、批准 grant、当前关联和助手绑定。

旧页面未带批次请求头时服务端生成本次批次 UUID；活动中同设备/项目/绑定/快照任务会按 v2 来源去重，避免重复点击产生并行任务。当时返回标准 `R<T>` 的R200会使原页面直接刷新并提示成功，这不符合“排队不能算成功”的要求，已由C77修正；逐项目实际状态/编号仍保留在响应数据中。Node 多 profile 轮询随后按各项目绑定领取。

API 严格编译0警告错误，未修改原前端/生成页面；真实跨项目 OAuth、MySQL和页面浏览器验收仍待执行。

## 原旧路由验收

C75在真实 `WebApplicationFactory<Program>` 下验证原旧批量 POST 地址：匿名请求返回原统一 `R` 登录拒绝、HTTP 200 包装、`Cache-Control: no-store`，没有404或绕过认证。该专项与原关系选项/首页接口回归共7项通过，使用不可达回环数据库配置，不创建库、账号或任务。

## 去重回归收口

C76修正同来源活动任务去重后的专项断言，并重跑来源专项29项；随后重跑API全量，严格155通过、49条件跳过、0失败。去重规则保持：同设备/项目/绑定/配置摘要的活动v2任务复用原任务，混合普通/v2确认仍拒绝。当时“已完成任务不阻断新批次”的结论缺少实际终态覆盖，C77后续复现出阻断问题并修复，不能用C76的155项替代该证据。

## C77：原设备上下文、实际任务编号及假成功修复

起点c90154c。按原Java `IndexController.sync_runtime_all_projects` 与 `ConfigPageBusiness.buildDeviceGenerateContext` 追踪，设备来自 `PowerInterceptor.getCurrentSysParams().getYzManagerDevicesId()`，不读取parentId。原uni-app共享请求确实发送编码sysParams头，项目列表parentId可能为空、undefined或其它父实体编号。C74虽然路由恢复且匿名测试通过，实际设备语义并不兼容；本次明确修正，不能称C74已经完成业务接线。

修复包括：

- 旧POST沿单一、有界、无重复字段的sysParams头取规范字符串设备ID；parentId仅兼容原列表上下文，不回退为设备。缺设备、错误类型、溢出/前导零、重复头/键、附加设备query及正文在查库前拒绝。拥有权仍由实际读取器复核，头字段不授予权限。
- 两个批量协调入口使用 `EnqueueRuntime` 返回的实际 `AssistantTaskView`，同步回传真实任务ID和状态。不能在活动去重后回传未持久化的新推导UUID，也不能把leased/applied等状态固定写成queued。
- 队列先核对精确UUID的原快照，再考虑其它活动任务；UUID冲突不能被另一条活动任务掩盖，重试精确旧UUID仍回旧结果。已有applied/rolled-back/manual-recovery回执的任务不占用新批次活动槽；活跃同来源任务仍稳定复用。
- 原共享customButtonHandler收到R200会直接提示成功。因此旧批量入口的排队、失败、过期、回滚、未知、仅跳过及空集合返回R500并保留完整逐项数据，消息用中文说明及实际任务ID，不能把入队当成应用完成。只有至少一项实际applied且其它全部applied/skipped才可返回旧成功码。该兼容拒绝误报不等同于已经实现原按钮等待最终回执。

验证：新增31项专项通过、0失败/跳过；最终完整API严格186通过、49条件跳过、0失败（42秒），完整Agent严格444通过、25条件跳过、0失败（41秒）。包含原sysParams长ID、parentId独立性、坏输入拒绝，精确UUID冲突、leased实际编号、三种实际应用终态释放槽/旧UUID回读，以及原成功UI判定。文件队列真实落盘，身份/Scope为明确合成；没有真实MySQL/OAuth/用户工程或浏览器登录操作。语法/编译由严格测试命令完成，所有进程正常结束后才写文档。

证据：E:\git\metarill\.local\batch-contract-first.log、batch-contract-api-final.log、batch-contract-agent-final.log及TestResults同名TRX。命令为项目固定DOTNET_HOST_PATH下对Metarill.Api.Tests和Metarill.Agent.Tests执行 `dotnet test -c Release --nologo --disable-build-servers --verbosity quiet -warnaserror`，另用filter执行新增专项。

原前端页面、样式、生成模板、backend/generated和待批准AgentTaskMiddleware均未改。无需DDL/回填/前端再生成。批量持久意图、跨项目只读查询及最终汇总未实现，不能靠再次点击/新UUID当未知恢复；原按钮最终闭环继续保留未完成状态。设备控制通道豁免仍待此前明确批准。


## C78：持久批次与只读状态恢复

起点4cf8d2d。两个入队入口统一使用同一协调器：先以批次UUID固定服务端发现的项目清单，再为每项目分别保存绑定、快照和实际任务指向，最后写精确任务编号。每项快照独立有界文件，复用现有文件锁、刷盘与原子替换；不引入数据库DDL。批次归属固定实际Web账号、工作项目和原会话ID，重新登录不能冒领旧会话批次。

- 重试同批次沿原清单，不吸纳新增项目或延长原期限；任务到期不晚于批次期限。准备后配置、元数据或关联变化明确拒绝，不换新快照/绑定。
- 命中现有活动任务时，先落盘保存实际别名，再允许发布；旧别名完成后即使产生新的活动任务，恢复仍读原别名。原别名丢失明确报错，不创建替代任务。原意图落盘后的未知异常向外保留，不伪装明确失败。
- 新增 `POST /api/admin/sys/generation-assistant/runtime-batch/status`，严格接收 `{batchId}`，继续实际网页登录和管理员检查；每个配置项目重新认证同账号同会话的真实目标项目身份。查询仅回读原记录与任务，不进行发现、prepare、claim、入队或过期落盘；按当前时间只读投影过期。
- 回执增加batchId，原按钮客户端尚未预存UUID。仅服务器持久记录不能解决HTTP响应丢失后客户端找不到批次的问题，此项保持未完成。状态入口的R200只表示查询成功，不能由原通用按钮直接当作应用成功。

验证：完整API严格206通过、49条件跳过、0失败（43秒），完整Agent严格444通过、25条件跳过、0失败（40秒）；新专项覆盖真实临时文件队列重启、别名稳定、丢失/损坏不替代、账号/项目/会话隔离、配置/快照漂移、固定期限、容量及只读协调器查询。严格编译0警告错误。匿名真实Program状态路由返回登录拒绝/no-store。身份、Scope及时间为受控样本，没有真实OAuth、MySQL、本机工程应用或浏览器登录验收。

证据：E:\git\metarill\.local\runtime-batch-persistence-api-final.log、runtime-batch-persistence-agent-final.log及TestResults同名TRX；专项初轮19通过，后续补充期限与协调器状态用例包含于最终API。命令沿C77固定DOTNET_HOST_PATH，对两个测试项目执行 `dotnet test -c Release --nologo --disable-build-servers --verbosity quiet -warnaserror`。

原页面、样式、生成模板、backend/generated和待批准AgentTaskMiddleware未改；无需DDL、回填或再生成。客户端预存批次、原按钮等待和最终逐项汇总继续实施；设备控制三个动作的登记豁免仍待此前用户确认，不作旁路。
