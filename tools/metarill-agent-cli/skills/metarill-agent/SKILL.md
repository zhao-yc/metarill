---
name: metarill-agent
description: 使用 metarill-agent CLI 发现并调用已授权 YZP v8 项目的数据和业务功能。用于查询项目数据、执行标准页面操作或已开放业务动作；平台元数据配置与代码生成仍使用 YZP MCP。
---

# 操作 YZP 项目

通过项目自身的能力目录调用现有接口。目录随登录用户权限变化，不依赖 YZP 平台在线。

- 先执行 `metarill-agent profiles list`，选择用户指明的项目；多个项目且无法确定时先澄清目标。使用 `--profile 名称` 固定本次目标，避免改动全局默认配置。
- 未授权时执行 `metarill-agent auth login --url https://项目域名/api --profile 名称`，向用户展示 CLI 返回的授权网址和用户码。授权需由用户在项目网页完成，等待 CLI 成功后再继续。
- 用 `metarill-agent capabilities list --profile 名称` 寻找所需能力，仅对选中的能力执行 `capabilities describe 能力标识`。根据详情的请求 schema、字段用途、枚举和关联选择入口准备参数，不猜测接口、字段或权限。
- 用 `call 能力标识 --json @参数文件 --profile 名称` 执行。路径参数采用 `--id`、`--parent-id` 或 `--param 名称=值`；URL 参数也可在 JSON 对象提供，显式选项优先。DELETE 按请求 schema 发送 DTO body；GET 的 JSON 仅用于已声明的路径/查询参数。
- 部分更新时不提交的字段保持不变，明确清空才提交 `null`。批量、复制和自定义业务动作按能力详情填写，不把它们视作普通修改。
- 修改前先读取目标记录，再用同一参数执行 `call 能力标识 --json @参数文件 --preview --profile 名称`，向用户展示对象、当前值与拟修改内容。预览只校验并展示请求，不执行业务、不登记任务；不是服务端状态锁或批准票据，不代表用户已授权。
- 能力要求确认时，先核对 `confirmationMessage` 和用户授权的具体对象、操作及影响；只有本次操作已获用户明确授权后才加 `--confirm`。Skill 不替用户授权删除、结算或其他业务写入。
- 写请求发出前 CLI 会向 stderr 输出 `request_started` 与 `requestId`；可用 `--request-id UUID` 显式指定。记录该标识，用 `tasks show 请求UUID --profile 名称` 查询服务端任务，再通过业务详情接口回读结果。任务不能替代业务回读，查询也必须使用仍有效的授权。
- 写请求超时或网络中断时结果未知，先按错误中的 `taskQuery` 查询任务，再回读对象，不直接重发。`YZP_REQUEST_ALREADY_RECORDED` 或 `YZP_CONFLICT` 也先查原任务，不能自动换新标识绕过防重复执行。任务保留 24 小时，未找到或过期不代表未执行；需回读后由人决定是否发起新操作。
- 根据结构化 `code`、`outcome`、`recovery` 处理失败：`query_task` 查询任务，`read_back` 回读对象，`fix_request` 核对请求并重新确认适用授权。权限拒绝或未登记能力时停止该操作，不改用其他入口绕过限制。CLI 不自动重试业务写入。

CLI 的 stdout 是 JSON，stderr 包含授权提示和错误；业务失败退出码非零。令牌由 CLI 管理，禁止读取、打印、转存或将配置文件放入项目仓库。设备可通过 `auth logout` 撤销，也可在项目网页管理已授权设备。
