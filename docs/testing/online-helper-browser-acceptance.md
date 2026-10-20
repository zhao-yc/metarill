# 在线助手原三按钮浏览器验收装配

本片是验收 Plan 和离线回执关联检查工具。它不联网、不登录、不填用户凭据、不批准设备、不启动助手/HTTP/浏览器，不执行计划中的 CLI，也不将结构校验通过称为现场验收完成。现场由根智能体另行安排独占窗口及真实登录交接。

## 精确现场范围

固定使用受支持业务 Demo 项目 `2200000000000100000`，从实际 `MetadataRepository.LoadProjectAsync` 对应的正常生成入口导出并严格预检。失败立即停止，不能修改 live 元数据、Seed、账号或物理表凑出通过。

- 新增或修改 config 元数据、项目、账号、角色、业务行：**零**。
- 新数据库、DDL、观测权限、业务 Host：**零**。
- 仅本轮自己新建的 Web 会话、一个带随机验收名的真实 OAuth grant 及其 Agent 会话、一个绑定、三个原请求，以及按真实执行形成的 **1–3 个组**。
- 平台 API 与原管理前端由根从未来最终冻结源码启动到新回环端口，四个封存进程不复用、不停止。Node helper 只出站轮询，不提供本地监听服务。
- 本轮结束只用原 `auth logout` 撤自己的 grant、原 `exit_login` 退出自己的 Web session。任务/组审计、候选、plan、journal、出箱和失败证据保留，不 SQL 硬删、不批量清会话、不自动删目录。

## 真实旧包的离线证明

已在独立普通 clone 固定 `896d10a2430c52ff395ca10bc9bfe06a28c1f904`。按该版本正常 CLI `package`，直接读取其 `backend/metadata/demo.json`，未手改生成文件或改标签制造差异，得到 **487 文件 FullApplication**。

随后用 `750dddc934e1c10169d38e2a38d193fb88af049d` 的当前 CLI 严格 `update target-identity` 读取成功：Demo / `Acceptance.OnlineBrowser` / MySQL。当前正规候选为 492 文件，双方元数据摘要均为 `58ed1e8a8db53a9cbf9fcbd207bc64621a2340083383416e7b047e47e8d97add`。

已仅预览：Config 需要 Runtime，Runtime 需要 Config，Pages 需要 Config 和 Runtime。**没有执行 Apply。** 私有日志位于本片 clone 的 `.local/online-helper-browser/baseline-896-{build,package,identity}.log`、`current-cli-build.log`、`current-candidate-package.log`、三个 `*-preview.log`。它们只证明这两个冻结版本的离线兼容，不代表根后续 14cf 或更新版本已经验证。

可复现的参数数组如下，路径由计划明确提供：

```text
dotnet <旧CLI> package --metadata <旧普通clone>/backend/metadata/demo.json --output <本轮外部目录>/target --namespace Acceptance.OnlineBrowser --source-root <旧普通clone>
dotnet <当前CLI> update target-identity --target <本轮外部目录>/target
dotnet <当前CLI> package --metadata <相同旧目录元数据> --output <独立当前候选> --namespace Acceptance.OnlineBrowser --source-root <当前可信源码>
dotnet <当前CLI> update preview --target <旧目标> --candidate <当前候选> --actions <原单动作> --plan <新计划>
```

现场必须以根最终冻结提交、对应 CLI 及依赖发布目录重新预检，记录 source/CLI/包/三层清单摘要；旧包不能当作当前发布结果。

## Plan 工具

`tools/acceptance/online-helper-browser.mjs` 仅有 `plan` 和 `verify` 两种模式。私有 spec 必须明确 runId、回环 API、从真实登录交接取得的 accountId、固定当前/旧提交、源根、CLI/dotnet 绝对路径、外部 fixtureRoot、命名空间。没有密码、令牌或连接字段。示例格式可从对应单元测试的 `fixture()` 阅读；其中账号与路径是纯离线反例，不能拿去当现场身份。

```powershell
node tools/acceptance/online-helper-browser.mjs plan --input <私有spec.json> --output <fixtureRoot>/control/plan.json
node tools/acceptance/online-helper-browser.mjs verify --input <私有plan与evidence汇总.json> --output <fixtureRoot>/control/new-structure-report.json
```

计划输出固定原 OAuth / bind / poll / revoke 的 argv 数组，**不会自动运行**。根执行前必须设置 `METARILL_AGENT_CONFIG` 到计划自己的凭据文件；不接触用户默认 profile。目标、work、工具/证据在源码根外的兄弟目录，例如 `E:/git/metarill-contrib/online-helper-browser-<nonce>/`。源码根内部 `.local` 不能作为绑定 target/work。

工具只新建并 Flush 自己的报告，已有文件拒绝覆盖，路径链接拒绝。现阶段没有安全自动创建全部现场的产品入口，因此不包含登录或设备批准自动化；按 computer-use 登录交接完成后，再由根捕获真实响应及现有本机文件。

## 原交互和现场捕获

三个原按钮仅在平台管理前端首页；不能搬到业务 Full 首页。main 的按钮没有 busy/disabled，调用 `start()` 不 await；bridge 请求 `showLoading:false`，独立 UUID 的轮询不产生遮罩，因此一个任务等待时仍可确认另一个按钮。验收不要求暂停助手或必须合为一组，1–3 组均按实际记录，不能为易计数修改 UI。

现场按原确认分别点三按钮，收取：

1. 三个原 UUID、单 action、发布 metadata SHA 和 request SHA。保持同一真实账号/项目/绑定，不改变 live 元数据。
2. 每个真实组的原 UUID 集合、精确 actions、plan SHA、ticket SHA、本机回执及服务端终态。每个原请求恰归一组，动作不超过用户分别确认的集合。
3. 对实际写入组，用原固定 C# `apply-status` 严格核 committed 和事务 ID，并保存其 journal 摘要。工具不把服务器自报 applied 当作独立文件证明。
4. 合法 `unchanged` 没有新 journal。必须保存原 CLI 零差异回执、原 plan 无 create/replace/delete、当前同 CLI 再预览同 plan SHA 和零差异；明确该按钮本轮未写文件，`unknown` 不能猜成功。
5. 固定四项保护文件的真实前后 SHA：`HostCustomization.cs`、`appsettings.local.json`、`DemoCustomerService.cs` 手写 partial、`frontend/manifest.json`。新建的自有验收标记只写手写/私有位置，绝不手改 `.g.cs` 或受管模板来制造差异。
6. 原三个完成 toast 与各 UUID 的真实浏览器外部复核、截图 SHA。结构工具不 OCR、不替根认定截图内容；必须保留真实原截图及浏览器操作记录。

还需现场验证：取消不入队、只预览不写、原依赖提示不扩大动作、受管文件的后来编辑被拒且不误报完成、离开/身份切换不显示迟到成功。漂移阴性使用独立自有目标，保留故障，不污染正例或自动恢复用户编辑。

## 离线校验边界

`verifyReceiptStructure` 只检查关联：三动作/UUID、账号/项目/grant/绑定、同快照、1–3 组和精确集合、服务端/本机计数及摘要、committed/NoOp 证明形状、四保护摘要与浏览器外部复核。始终返回 `acceptanceCompleted:false`、`requiresLiveCaptureAndRootReview:true`，不能用手工 JSON、合约替身或本测试当作实际 Apply / HTTP / 浏览器完成。

本轮没有 DB、HTTP、OAuth、服务或浏览器实测，没有更改原三入口、框架门禁、元数据或生成产物。平台 BackendOnly 自举、RuntimeA、三 partial、5 Custom 和 Agent 门禁不因本工具而关闭。最终 Node 内置测试 **8/8 通过，零跳过**，并加入现有 CI Node 测试入口；模块语法检查通过。首轮 7 过 / 1 失败仅为新测试把绑定所需的 `--mode apply` 参数误认为执行 `update apply`，纠正断言后通过，日志分别保留在 `.local/online-helper-browser/runner-offline-first.log` 与 `runner-offline-final.log`，不累计重复次数。
