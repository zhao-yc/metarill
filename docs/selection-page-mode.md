# type=2 独立多选页增量

本切片只恢复已存元数据 `config_page.type=2` 的独立选择页。它不启用 `MultiSelectPage` 或 `MultiCheckBoxDatabase` 字段，不承担名称辅助列派生、名称变更传播或非冗余投影；这些字段仍进入生成能力缺口清单。普通列表和已有单选 `select=1` 流程保持原合同。

## 旧协议证据

- 旧 v8 `service/yzp/v8/UniappService.java:1099-1158`：弹层嵌入目标生成列表，`type=2` 确认发 `onPopSelect(row[])`；父组件分别按 `item.id` 和真实名称字段映射 ID 数组、名称数组。`onPopClose` 只关闭。
- 同文件 `:2292-2348`：跨页草稿、仅看已选时发送当前 `selectedIds`；空的仅看已选直接返回空列表，不能变成查全部。
- 同文件 `:2542-2567`：原 `selectedIds` / `selectedNames` 接受数组或 JSON 数组文本，保留 `selected-name-field`。
- 旧 `JavaService.java:2765-2807` 与 `config/JsonToStringArraySerialize.java` / `StringToJsonArraySerialize.java`：`_ids` 的实体存储是 JSON 数组文本，HTTP 是数组，不是逗号拼接。
- 旧 `JavaService.java:2276-2346` 还生成名称 JSON 辅助列；非冗余读取另依赖 `NonRedundantReadPlanCompiler` 的多值关系投影与筛选。这些尚未在 Metarill 全部移植，因此没有直接开放完整多值字段。

## 当前合同

仅 `type=2` 页面生成 `createSelectionPageMixin`，继续接受上述三个原 props；`selectedNames` 保留参数但不作为可信行缓存。初始已选与每次确认都通过固定目标页的 `POST /api/admin/auto/{table}/{page}/selection/selected` 重新读取完整本页 VO。请求只有 `selectedIds` 字符串数组和 `parentId`，未知键拒绝。没有请求表名、列名、目标页、SQL、连接或项目选择入口。

原始已选数量最多 200，去重保持首次选择顺序；负数、零、表达式、溢出 ID 拒绝。分页每页维持既有最多 1000，`type=2` 页码最多 1000000，`/page` / `/count` 的 `selectedIds` 同样最多 200，不截断。单次选中数量超过上限明确报错。

新回读端点检查真实目标页 list 权限及 Actor；生成的 `/page`、`/count` 和已选回读共用项目、软删、固定页范围及真实父记录检查，读取不申请记录写锁。任何不存在、软删或越界已选 ID 都使完整回读失败，不返回合法子集冒充确认成功。确认前重新回读，不能将选择后已删除的行从旧缓存回传。

页面保留当前页全选、跨页草稿、仅看已选、选好了、关闭；全选只作用于当前页。确认成功发原 `onPopSelect(row[])`，随后 `onPopClose`；路由选择模式也发 opener 的 `selected(row[])` 并返回。ID 全程保持字符串，关闭/取消只失效回调与关闭，不发送选择或业务保存请求。只读禁止勾选和确认。父记录、项目、登录和卸载变化取消旧请求；无会话未建立请求时退出加载并报明确中文错误。

这只是独立目标页的安全能力，没有来源字段控件范围或来源父到目标父的完整接线。完整 Multi 控件不能通过此页能力推断为已支持。需要弹层的手写父页可继续用 `selectedIds`、`selectedNames`、`selected-name-field` 和 `onPopSelect` / `onPopClose`；目标父通过真实 `parent_id` 传递，未知父映射不得猜测。

## 验证和集成边界

已执行实际生成 Vue 脚本：跨页勾选、全选本页、仅看已选、长 ID、原 JSON 数组文本、确认前完整新回读、取消后迟到响应、失败清旧名称保留 ID、项目/卸载隔离、无会话和只读。相关选择页、原生成页与主子权限定向 24 项通过，完整现有前端 Node 229 项通过。

真实 Server-200 MySQL 5014 候选样板经过实际生成编译与 HTTP：新端点逐项完整回读/顺序、分页/总数、外项目、软删、另一父范围、真实父记录、拒权、数量/页码上限、动态目标拒绝、初次选择后删除均通过。未使用本机 Docker，也未连接旧私有服务。

完整 Generation 回归为 79 通过、2 个专用 Fresh 测试未启用而跳过、4 个打包检查失败。失败位于主线已有新增前端资产的换行和 verify 条件编译检查；已交给主线保留详细证据，本切片不修改 packager、CLI 或这些资产。完整生成套件不能标为全绿。

新增共享资产只有 `common/yzp_runtime/selectionPageMixin.js`。主线集成时需将它加入受控打包资产与运行时清单，并统一再生成。当前切片没有修改打包器或旧生成产物，未运行 HBuilderX、浏览器或设备，不据此宣称独立下载包或跨端验收完成。
