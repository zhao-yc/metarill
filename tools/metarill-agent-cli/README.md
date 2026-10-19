# Metarill Agent CLI

此工具迁自 YZP Agent CLI 0.2.0，保留版本 1 能力目录和 X-Yzp-Agent-* 协议头以便兼容。当前 C# 开发版已通过设备授权、能力目录、撤权和持久任务的真实 CLI 联调；独立生成项目仍需自行接入并验收这些服务，不能从 CLI 测试推断所有项目已支持。


Node.js 20+ 通用命令行工具，无第三方运行依赖。通过项目自身的设备授权和能力目录，让 Agent 使用已有后台账号的权限调用 Metarill 页面接口和显式开放的业务动作。

## 安装与运行

```powershell
# 在本目录运行测试并构建本地可安装包。
npm test
node -e "require('node:fs').mkdirSync('dist', { recursive: true })"
npm pack --pack-destination dist

# 使用实际生成的包名安装；尚未发布公共 npm。
npm install -g ./dist/yinzon-metarill-agent-cli-0.2.0.tgz
metarill-agent --help

# 也可直接从仓库运行。
node bin/metarill-agent.mjs --help
```

打包命令中的缓存如不可写，可增加 `--cache dist/npm-cache`。包只包含入口、运行源码、Skill 与本说明，不包含测试或本地配置。Windows、macOS、Linux 使用同一套 Node 源码；当前测试环境与平台实测状态以项目验收记录为准。

## 授权与多项目

```bash
metarill-agent auth login --url https://项目域名/api --profile demo --name 我的开发电脑
metarill-agent auth status --profile demo
metarill-agent profiles list
metarill-agent profiles add other --url https://其他项目域名/api
metarill-agent profiles use demo
metarill-agent auth logout --profile demo
```

登录会把原始浏览器授权网址和用户码写到 stderr，并按项目返回的间隔轮询。CLI 不打开本地端口，也不代用户批准授权。`authorization_pending` 继续等待，`slow_down` 增加 5 秒间隔，拒绝或过期退出非零。取消可按 Ctrl+C。

配置默认位于 `~/.metarill-agent/config.json`，可用 `METARILL_AGENT_CONFIG` 指定独立文件。凭据同时绑定 origin、API 基础路径和项目 ID；已有配置名称不能改绑其他 URL。POSIX 使用目录 0700/文件 0600，Windows 使用当前用户 SID 限制 ACL。CLI 保存访问/刷新令牌，操作前轮转刷新并原子写回。不要将配置提交到仓库或同步给其他人。远程项目应使用 HTTPS；HTTP 适用于可信开发网络。

`auth logout` 会先撤销服务端授权；无法访问项目时，可用 `--local-only` 仅删除本机凭据，再到网页撤销。`profiles remove 名称` 只删除本机配置，不撤销服务器授权。多进程共享同一配置时，刷新和写入使用排他锁；进程异常退出遗留 `.lock` 时，确认其他 CLI 已退出后才删除锁文件。

## 发现和调用

```bash
metarill-agent capabilities list --profile demo
metarill-agent capabilities describe page:42:update --profile demo
metarill-agent call page:42:list --json '{"pageNo":1,"pageSize":20}' --profile demo
metarill-agent call page:42:update --json @update.json --preview --profile demo
metarill-agent call page:42:update --json @update.json --profile demo
metarill-agent call page:42:detail --id 123 --profile demo
metarill-agent call action:approve --json @approve.json --confirm --profile demo
```

`--json` 接受内联 JSON 或 `@文件路径`。JSON 体保留字段存在性和显式 `null`，也支持 DELETE 的 `{ "id": 123 }` DTO。路径参数用 `--id`、`--parent-id` 或 `--param 名称=值`；路径/查询参数也可在 `--json` 对象中提供，显式选项优先。URL 参数只有在请求 schema 未声明同名 DTO 字段时才从 body 移除；GET 的 JSON 仅允许已声明的路径/查询参数。未声明的显式参数会报错；必须以实际能力详情为准。

能力目录协议为 `1`，标识为 `page:<页面ID>:<操作>` 或 `action:<动作标识>`。CLI 先核对当前项目身份和用户能力目录，再获取目标能力完整详情。标准路径必须位于当前项目的 `/admin/` 内，拒绝外部 URL、路径穿越和所有重定向；业务请求发送 `X-Yzp-Agent-Capability`，确认请求另发 `X-Yzp-Agent-Confirm: true`。

`confirmationRequired` 能力必须加 `--confirm`；Agent 应先检查详情中的 `confirmationMessage` 和用户对本次操作的授权。CLI 不放宽任何后端权限检查。stdout 输出 JSON（`call` 保留项目 `R` 响应），stderr 输出中文提示或错误 JSON。HTTP 失败、`R.code` 非 200、业务拒绝均退出非零；错误保留脱敏后的业务原因，隐藏认证字段与当前令牌。`--timeout 毫秒` 默认 30000，最大 300000。请求发出后不会自动重试，写请求超时需先查询业务结果。

## 修改预览与任务恢复

`call --preview` 使用同一能力目录和参数绑定，返回拟发送的能力、HTTP 方法、路径、查询参数、JSON 体和确认说明。它会读取身份与能力目录，但不调用业务接口、不登记任务，也无需先添加 `--confirm`。预览不是服务端状态锁或批准票据；它不自动读取修改前数据、不计算数据库影响、不保证预览后数据仍未变化。Agent 应先查询目标记录，把当前值和拟修改内容展示给用户，再按已获授权执行。

例如在示例页已确认页面 ID 为 `42`、DTO 支持 `notes` 时：

```bash
# 查询并核对记录，update.json 只包含本次获准修改的字段。
metarill-agent call page:42:detail --id 123 --profile demo
metarill-agent call page:42:update --json @update.json --preview --profile demo

# 核对对象、内容及用户授权后执行；也可以省略 --request-id，由 CLI 自动生成 UUID。
metarill-agent call page:42:update --json @update.json --confirm --request-id aaaaaaaa-1111-4222-8333-bbbbbbbbbbbb --profile demo
metarill-agent tasks show aaaaaaaa-1111-4222-8333-bbbbbbbbbbbb --profile demo
metarill-agent call page:42:detail --id 123 --profile demo
```

能力的 `operation` 为 `list`、`detail`、`count`、`options` 时按只读处理，即使 HTTP 方法是 POST 也不登记写任务。其他操作在完整参数校验后生成或接受 `--request-id UUID`，先向 stderr 输出一行 `request_started` JSON，再通过 `X-Yzp-Agent-Request-Id` 发送。读取能力不能指定 `--request-id`。成功 stdout 仍是原业务 `R`；服务端可在 `map.requestId` 返回请求标识。

服务端在项目与账号范围内把请求标识绑定到能力、方法、路径、查询和请求体摘要，任务保留 24 小时。重复标识返回 `YZP_REQUEST_ALREADY_RECORDED`，同一标识但请求内容不同返回 `YZP_CONFLICT`；两者均不再次执行，也不回放原业务响应。CLI 不把冲突视为成功，不自动换新标识重试。旧服务没有任务接口时，CLI 的 UUID 仅可供排查，不能据此认定服务端具备防重复执行能力；试点前需先更新项目运行时。

`tasks show` 每次先检查当前项目身份，再用当前授权查询服务端；撤权后不返回本机缓存。结果中的 `status` 为 `running`、`succeeded`、`failed` 或 `unknown`，`outcome` 为 `applied`、`not_applied` 或 `unknown`。任务摘要还包含能力、时间、HTTP/业务状态及可选结果 ID，不包含原始请求或完整业务响应。查询完成后仍应通过原业务详情接口回读。

网络中断、写请求超时或响应无法解析时，错误包含 `requestId`、`outcome: "unknown"`、`retryable: false` 及 `taskQuery` 提示；先查询任务和业务数据，禁止盲目重发。任务不存在或已过期也不能证明请求未执行，需要回读后由人决定是否发起新的操作。

错误继续保留原 `error`、`message` 字段，另外只提取服务端 `map.agentError` 或顶层 `error` 对象中的白名单 `code`、`outcome`、`recovery`、`requestId`、`retryable`。`recovery` 为 `query_task`、`read_back` 或 `fix_request`；CLI 始终不会自动重试业务写入。stderr 可能先有请求标识日志、再有最终错误 JSON，程序应按 JSON 行读取。

## 安装 Skill

```bash
# 安装随 CLI 交付的通用 Skill 到 Codex 技能目录。
metarill-agent skill install

# 从已授权项目下载动态 Skill，并安装到指定技能父目录。
metarill-agent skill install --project --profile demo --dir ./agent-skills

# 已存在时需明确允许更新。
metarill-agent skill install --force
```

默认安装到 `$CODEX_HOME/skills/metarill-agent/SKILL.md`，未设 `CODEX_HOME` 时使用 `~/.codex/skills/metarill-agent/SKILL.md`。`--dir` 是技能父目录，不是 SKILL.md 文件路径。项目动态 Skill 来自 `GET /admin/sys/agent/skill`，只含按需发现流程，不嵌入全项目 schema。安装不会修改 Agent 全局 MCP 配置。

## 服务接口合约

- `POST /agent/oauth/device`：表单 `client_id=metarill-agent-cli`、`client_name`；OAuth 原始响应。
- `POST /agent/oauth/token`：RFC 8628 设备授权或刷新表单；原始响应含 `access_token`、`token_type=Bearer`、`expires_in`、`refresh_token`、`project_id`。
- `GET /admin/sys/agent/me`：`R.data` 含 `projectId`、`projectName`、`accountId`、`accountName`、`grantId`。
- `GET /admin/sys/agent/capabilities`：`R.data` 为协议版本、项目身份与当前用户能力摘要；`/{编码后的能力标识}` 返回完整描述。
- `GET /admin/sys/agent/skill`：`R.data.content` 是动态 Skill 文本。
- `POST /admin/sys/agent/revoke`：撤销当前授权。
- `GET /admin/sys/agent/tasks/{requestId}`：查询当前账号的请求任务摘要；调用前仍检查授权，未找到返回结构化错误，不能解释为从未执行。

所有地址基于 `--url` 中完整 API 基础路径。CLI 不直接访问数据库，不修改平台元数据；此类开发工作使用 Metarill MCP。

## 在线本地生成助手

先完成真实设备授权，再用 `helper bind --target 绝对FullApplication目录 --source-root 可信Metarill源码 --cli 固定Metarill.Cli.dll --dotnet dotnet绝对路径 --work-root 独立工作区` 一次性绑定。默认 `preview` 明确无法写入；允许原按钮写入需绑定时明确 `--mode apply --allow-actions generate-project-config,sync-runtime,generate-all-page --confirm`，也可只选其中受支持动作。绑定后运行 `helper poll`，日常操作保持平台三个原按钮及确认，无需手动JSON或CLI应用。

本机只运行固定、复核摘要、无shell的C#CLI；不上传目录/连接/日志，不执行安装、SQL/DDL或服务启动。缺依赖回原按钮分别确认，仅同快照真实确认集合可合组；短ticket在应用前再核对grant与平台metadata SHA。预览/排队/未知不是应用成功。

回执先落本机出箱，服务器已保存但ACK丢失仍按原编号幂等重传；中断apply只核对严格原journal，再恢复同一个本机反查事务，证据不全或用户后来编辑保持unknown并保留现场，绝不盲目重新apply。具体授权、路径、TTL、恢复和实际验收边界见 [在线助手说明](../../docs/generation-assistant.md)。
