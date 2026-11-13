# Full Web 打包的 CI 兼容回归

2026-10-20 的历史红证：`8edaad9`，PR 运行 `39401459333`、Linux job `118556918246`，Generation 为 693 通过、14 失败、12 跳过，总计 719。本修复不把该运行改写为成功，也不把本机定向结果称作 Linux/Windows CI 已通过。

## 三类根因与边界

- 固定版本目录 5 项、角色 Runtime 门禁 5 项、平台 CLI 1 项：新 Full 安全物理合成早于原页面能力检查，先报基础设施页面拒绝，遮住原来的具体缺口及 BackendOnly 提示。完整包先校验原页面能力，再执行原严格物理合成，二者仍在任何 stage 写入之前；不放宽 Core，也不解除 Runtime A 或五处 Custom 门禁。
- 原固定版本 HTTP 候选 1 项：夹具以项目安全表页导出 Full，但用途是原开发宿主无数据库候选。改为显式 BackendOnly，保留实际构建、随机 loopback 宿主、原 POST/匿名/非法值断言；不借此宣称 Full 安全管理页已发布。
- 原壳更新 2 项：夹具已用 JsonNode 保留 profile，并未丢字段。工厂包把 authenticationProfile 写在中间，原更新器 PackageManifest 序列化到末尾，导致无实际改动也替换外层清单。只对齐工厂生产顺序，不修改 Payload、日志版本、恢复归属或原 unchanged/单项 create 断言。

新增反例证明没有其它前端 Gap 的项目安全页仍被物理合成拒绝，且不产生输出或 stage。新增工厂 Full 两轮 Preview/Apply 均为 unchanged，外层、前端和生成清单逐字不变，不创建事务日志。现有缺 profile 的旧开发 Full 升级、profile 冲突拒绝以及私有配置/AppID 保护测试保留。

## 本机证据

- 修复前 Shell 原类实际 6 项：4 通过、2 失败、零跳过，保留 shell-red-v1.trx/log；分别复现 unchanged 变成 committed，以及单个 create 多出外层清单 replace。
- 修复后首批 97/97、零跳过，启用 -warnaserror：FixedCatalogOverlayAuditTests 11、FixedRoleCheckboxCapabilityGateTests 18、PackageCliTests 1、TemplateVersionCatalogTests 30、LocalGenerationUpdateShellCreationTests 6、ApplicationAuthenticationPackageTests 10、MetadataCatalogCompositionTests 4、SecurityMetadataCompositionTests 17。证据 compatibility-green-v1.trx/log。

证据保存在开发克隆忽略目录 `.local/ci-full-web-compatibility/`。只运行文件、生成编译、CLI、原 Vue 及前述无数据库回环宿主测试；未访问数据库、启动额外服务、执行 DDL 或调整工作流。完整 Generation 和新远端 CI 仍由整合后独立运行，不移用本批结论。
