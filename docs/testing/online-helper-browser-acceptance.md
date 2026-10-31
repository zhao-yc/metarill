# 在线助手原三按钮浏览器验收装配

本文件保留初始Plan工具、2026-10-20原三按钮真实闭环及2026-10-29 C28受控原H5时序的分阶段证据，各批范围分别认定。下述Plan和离线回执工具本身不联网、不登录、不批准设备、不启动助手/HTTP/浏览器，不执行计划中的CLI；其结构校验不能代替后文实际现场。C28使用合成HTTP，不与真实助手或数据库权限证据混同。

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

## 2026-10-20 原三按钮真实浏览器与本机文件闭环

本批后端、固定CLI及可信源码绑定为 `d46ac893d4083f0b93cc73a55d03f668d41919a9`；原前端首轮同d46，实际错误修复后前端独立冻结为 `55650980f4d07f60905cf47e157ed050c9683175`。不能把该组合说成556后端全量实测。原uni-app页面、模板、CSS和确认方式保持，修复仅显式转接HBuilderX按需API。

真实浏览器依次完成原账号登录、默认设备/示例项目选择、本轮自有助手授权、原确认取消及三个原按钮分别确认。取消后独占队列任务和组均0。真实项目导出Full候选按单范围预览，三项均明确要求另两项；只读预览没有apply。三个实际UUID分别为 `b2a4d608-c6d1-4ebf-a08d-a6530f5eceef`、`56e64fdc-f6f0-4344-bcfd-19d8bd24e05c`、`200deaa1-490e-4c95-84d4-eff0a496d1e8`，恰好形成一个已确认三动作组 `fbd5027c-8d40-46aa-8e3a-eec6a087ebf1`，发布元数据SHA256 `c0d97d4e5bc2b5bcc7ee4fd380cf0e27dd294be70d0bbcbb275ed577d62844df`。

三个原HTTP查询均200、`status=applied/outcome=applied/applied=true`，同组回执实际写26文件、删0、保留19；固定原C#CLI独立 `apply-status` 返回 `committed`，事务 `4a68538512814678ab64c58231ace9fe`，计划SHA256 `d1a58a6c79ff132bdbbe774d3649234bf06faeefd5d1a9f46cb27b4da22d7a8e`。手写Host扩展、私有配置、DemoCustomerService partial和manifest AppID四文件前后摘要全部相同。

主组真实DOM捕获到“同步运行时完成”“生成全部页面完成”，截图中完成提示与依赖窗有重叠；未宣称主组三提示均被独立截图捕获。另行写明补验计划后，只新增一次原“生成项目配置”请求 `2f4a2c91-b6c5-4bb1-911d-43391ef8d05e`，单独捕获其真实完成提示。该补验是合法NoOp：写0/删0，545个目标文件（包含原事务记录）前后摘要全相同，固定CLI再次preview得到同计划SHA256 `aeb08be45a202f6784739d9eb424c3a991321d2a6e854815a48b26f9a4c54a7f`、0项变更，journal数量仍1。额外请求不计入原三请求，不以没有新journal猜测成功。

独立自有漂移阴性在正常preview之后修改其受管文件；原CLI apply退出1并明确拒绝，后来编辑摘要保持、journal0，不污染正例或自动恢复。离页/切换身份迟到反例尚未现场执行，已有离线测试不移用为浏览器结论。本批并未满足工具要求的“主组三提示各自截图”，没有伪造手工证据让结构校验冒认完整浏览器验收。

私有 `metarill-contrib/online-helper-live-a2e6b522e37e46b9/control/live-integrated-receipt.json` SHA256 `73d223c602e94918d753fa588b2a06d10faf64098de336a29993b044a66ca271` 关联原HTTP、CLI journal、文件摘要、真实截图和失败记录。原CLI logout已撤销本轮grant；原网页再查本连接和绑定均不可用，配置无访问/刷新令牌。原界面退出后实际返回根路由登录页、自己的登录槽已无；首次错误等待custom/login路由的超时保留，未重试注销。仅本轮8088/5202与自己的Node轮询已按PID/路径/启动时间停止，四封存服务未动。队列任务、组、候选、plan、journal、失败与阴性现场保留。

该批是原三按钮到独占工程文件事务的实际闭环，不是业务宿主、独立安全管理CRUD、真机、性能或生产部署验收。没有写元数据、账号、角色、业务行或DDL，没有增加观测权限；完整计划继续进行。

## 2026-10-29：原H5迟到时序受控浏览器子集（C28）

固定来源 `804d1e7e7873e5660a3b1b52017a525ea998ab4f`，本轮没有更改产品代码、前端源码、生成模板、页面或交互。HBuilderX 5.33.2026102401实际执行 `publish web --project E:\git\metarill\.local\paired-baseline-20261028\metarill-source\frontend\metarill_uni_admin`，编译并导出Web成功。最初uniCloud本地调试提示workspaceFolder不存在，但本轮Web构建继续成功；不以该提示宣称云服务已启动。

这批使用ego-browser实际操作当前原登录页、项目选择、原首页三按钮、原确认、头像菜单、修改密码页和退出后重新登录。HTTP服务仅监听127.0.0.1:5306和8088，账号、会话、项目、绑定及任务均为内存合成数据；不读取私有配置、不转发真实API、不连接Server-200、不调用OAuth或CLI更新、不写业务库。它证明浏览器中的原代码时序，不能替代前述真实助手/OAuth/文件事务或数据库权限证据。

| 场景 | 实际浏览器与网络证据 | 结果 |
| --- | --- | --- |
| 正常完成阳性对照 | 原“生成项目配置”确认后产生请求5b127ece-eac1-45fd-9fb0-bc6f1c23134b，观测到实际状态GET挂起后释放applied形状；原toast DOM与截图出现“生成项目配置完成” | 通过；合成完成响应，不表示真实工程已写入 |
| 离开首页再返回 | 原“同步运行时”请求6f1e6734-8559-4c60-8bbc-533f21e35121挂起期间，通过原头像“修改密码”离页；再释放响应，原请求诊断记录stale且无迟到完成。原首页按钮返回后同UUID第二次GET读回、DOM记录“同步运行时完成” | 通过；仅新增一次状态查询，没有重发动作 |
| 退出后切换账号 | 原“生成全部页面”请求b31604fc-a1b5-4c57-9150-229e42892e66保持挂起；原退出确认后，由合成A账号切到合成B账号并选同一项目，再释放旧响应 | 通过；原stale收尾，未出现“生成全部页面完成” |
| 切换项目 | 合成B账号的原“生成项目配置”保持挂起，通过原工作区按钮从甲切到乙后释放旧响应 | 通过；原stale收尾，新项目无旧完成提示 |

直接观察原 `$yzp.getRequestLoadingState()` 的只读诊断和MutationObserver捕获的真实toast/弹窗DOM，不替换showToast、组件生命周期、请求回调、认证方法或可见状态。三个阴性均关联原路径/UUID的stale事件，最终在途请求0、加载框不可见。4个合成动作共5次状态GET；返回首页的一次恢复解释额外GET，未声明接口0、重复动作0。三种上下文分别覆盖一个原动作，不等同三按钮与全部上下文的全组合矩阵。

已查看四张原截图：阳性、修改密码页、合成B账号首页及项目乙首页。最后一张保留先前“已排队”提示淡出效果，它不是迟到成功；没有修改原toast样式或删除截图内容。返回首页时一次按短暂toast选择器等待超时，随后只读检查发现原DOM观察已捕获完成且同UUID恰多一次GET；未重发或伪造成功。初始夹具getMenu大小写/响应层级遗漏已在零任务阶段修正，未因此改产品菜单或放宽原响应校验；首次沙箱无法连接ego_cli后按诊断使用同一浏览器沙箱外连接，未另开浏览器。

私有证据目录 `.local/helper-late-browser-20261029` 保存合成HTTP夹具、四截图、positive/leave/resume/account-switch/project-switch回执、final-observation与receipt。receipt逐项固定492份原前端文件、155份Web产物和4张截图摘要，来源不变；receipt SHA256为 `d86c3df5615d7902788359f6128ae16e191bed8bcb41edbabcb8b1658f038921`。ego操作脚本保留在本聊天可视化目录helper-browser-01至14，实际任务空间21已finish，原退出完成，仅本轮站点localStorage已清除。仅本轮PID4788的夹具已核对命令行后停止，5306/8088均无监听，其余服务未动。

C28仅将本受控浏览器子集标完成。C09仍需真实身份/助手迟到组合与完整管理工作台场景；C05/C06、跨端、性能及MySQL首版整体仍未完成，不将本批合成账号记作真实权限或数据库验收。
