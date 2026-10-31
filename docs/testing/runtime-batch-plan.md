# 批量运行时逐项目只读计划

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

旧页面未带批次请求头时服务端生成本次批次 UUID；活动中同设备/项目/绑定/快照任务会按 v2 来源去重，避免重复点击产生并行任务。返回仍是标准 `R<T>`，原页面继续刷新列表；逐项目状态和任务编号保留在响应数据中。Node 多 profile 轮询随后按各项目绑定领取。

API 严格编译0警告错误，未修改原前端/生成页面；真实跨项目 OAuth、MySQL和页面浏览器验收仍待执行。
