# Metarill · 元溪

基于 YZP 主框架重写的开源元数据应用开发平台。平台和生成应用采用 C# / ASP.NET Core，管理端保留 HBuilderX uni-app Vue 3 工程。Apache-2.0 许可证。

## 当前状态

功能完成情况与后续待办统一见 [实施进度表](docs/project-progress.md)。每完成一项，同步更新状态及验收依据。

当前是首期 MySQL 开发版，尚未达到完整替代 YZP 的发布标准。生成列表、详情和独立前端导航壳已切回原 YZP v8 模板与组件，C# 后端适配原接口；已实测原单表页面的 PC 分页、筛选排序、校验、保存、复制、删除及移动视口。复杂关系、完整配置流程和真机仍须继续逐项复验，详见[原前端恢复及证据边界](docs/frontend-preservation-correction.md)。已完成真实 MySQL 的登录→表字段配置→结构预览/执行/核验→生成独立应用→业务 CRUD 闭环，以及菜单树、权限、文件、导入导出、MCP 和 Agent 的核心接口验收。具体证据与剩余范围见 [验收台账](docs/acceptance.md)。

前端已配置 Metarill 的 DCloud AppID，HBuilderX Web 发行构建成功。真实浏览器已验证登录、项目选择、菜单、配置表列表与详情回读，以及临时表保存、批量字段、结构预览/执行/零差异核验、生成下载和元数据删除。原选择器已确认 MIT 许可并接回，真实浏览器从零选择后台、建表、回读默认字段已通过；完整业务页面/真机、其他数据库与同条件性能对照尚未验收。富文本按用户决定接自有 Tiny Cloud，key 暂留空，实际 Cloud 加载与编辑待配置后验证。

独立生成应用随包提供共享 XLSX 导出和本地文件库。文件基础结构从平台元数据确定性派生，通过包内命令显式安装；生成业务记录与文件引用共用一个数据库事务。真实隔离 MySQL 验证了上传、公开/受保护下载、引用阻删和非法引用整体回滚。平台与示例业务库已分别接线文件服务，并以同一业务数据库事务维护引用；全新空库的显式安装已实际通过。原图片上传协议已恢复，公开地址由可信服务端配置生成。独立 FullApplication 包已装配 uni-app 宿主，开发登录、菜单与生成 list/detail 的真实 HTTP 脚本链路通过；该包已通过 HBuilderX Web 发行和真实浏览器登录、菜单、父子 CRUD、筛选排序、父只读及注销；独立 type=2 多选页另实测两页21项、取消/只读及确认前完整回读；冗余 MultiSelectPage 字段已在重新导出的完整包实测取消、跨页四项、确认/保存回读、父范围失效及空数组，其他多值子集仍按缺口处理。远程 List 另验甲128/乙105项、三页、字面搜索及父切换；超过5000项候选、文件/导入导出浏览器和真机仍待验收，Agent 尚未随包集成。详见 [独立应用包](docs/standalone-application.md)。这些分批接口与早期包记录不代表恢复原模板后的全部流程已重新验收；当前实际原界面证据以纠偏记录为准。

## 架构

- 元数据驱动结构同步、强类型 Entity / DTO / VO / Mapper / Service / Controller 及页面生成。
- 同一程序集内以 C# `partial` 分离生成文件与手写扩展；重复生成保护手写内容。
- 普通 CRUD 由 Service 完成；跨对象流程按需建立手写 Business，并共享同一数据库事务。
- FreeSql 负责普通持久化，MySqlConnector 作为 MySQL 驱动；数据库差异集中在适配层。
- 平台库、示例业务库和其他项目库分别配置。未配置的项目明确失败，不回退到平台库。
- 结构变更必须显式预览、确认执行、核验并记录；启动不会自动修改数据库。

## 开发入口

先安装 [global.json](global.json) 中声明的 .NET SDK、Node.js 24 和 HBuilderX。安装位置与版本必须实际核验。

1. 按 [配置示例](backend/appsettings.Example.json) 创建不提交的 `.local/development.settings.json`。提前准备独立的 MySQL 平台库和业务库，以及仅有对应库权限的账号；连接串中的库名须与 `ExpectedDatabase` 完全一致。
2. 设置随机管理员密码，为不同服务进程分配独立的 `WorkerId`。部署时通过服务端 `Metarill:ProjectDatabases` 按项目 ID 配置额外业务库；不能由客户端提供连接串。
3. 执行以下命令。初始化会显式预览、执行首期结构并补缺失种子；重复初始化不会重置密码或复活已删除的元数据。普通启动不执行结构同步。

```powershell
pwsh -NoProfile -File scripts/dev.ps1 initialize
pwsh -NoProfile -File scripts/dev.ps1 generate
npm run sync:write-schema --prefix frontend/metarill_uni_admin
npm run sync:custom-routes --prefix frontend/metarill_uni_admin
pwsh -NoProfile -File scripts/dev.ps1 start
```

API 默认地址为 `http://127.0.0.1:8088/api`，内嵌 MCP 为 `/api/mcp`。日志与停止命令：

```powershell
pwsh -NoProfile -File scripts/dev.ps1 logs
pwsh -NoProfile -File scripts/dev.ps1 stop
```

前端先在其目录执行 `npm ci`，申请并配置自己的 DCloud AppID，再使用 HBuilderX 打开工程。已有一键入口见 [.vscode/tasks.json](.vscode/tasks.json) 和 [.codex/environments/environment.toml](.codex/environments/environment.toml)。完整前端要求及许可状态见 [前端说明](frontend/metarill_uni_admin/README.md)。

## 验证

`scripts/dev.ps1 test` 遍历全部后端测试项目；`npm test --prefix frontend/metarill_uni_admin` 验证前端运行时，Agent CLI 另执行 `npm run check --prefix tools/metarill-agent-cli` 和 `npm test --prefix tools/metarill-agent-cli`。

真实数据库测试仅接受隔离库名 `metarill_test` / `metarill_business_test`，通过 `METARILL_TEST_SETTINGS` 指向私有测试配置，并设置 `METARILL_PLATFORM_METADATA` 为平台元数据文件的绝对路径。未提供时相关用例明确跳过，不能算真实业务验收。CI 编译并运行所有测试项目，真实 MySQL 验收在核实身份后的 Server-200 独立测试库执行。

## 进一步阅读

- [独立应用包与前端宿主](docs/standalone-application.md)
- [已有应用的本地源码更新](docs/local-generation-update.md)
- [type=2 独立多选页合同](docs/selection-page-mode.md)
- [架构与实施边界](docs/architecture.md)
- [前后端契约](docs/integration-contracts.md)
- [索引聚合配置及限制](docs/index-workbench.md)
- [来源与脱敏基线](docs/source-baseline.md)
- [前端许可清单](frontend/metarill_uni_admin/THIRD-PARTY-NOTICES.md)
- [Agent CLI](tools/metarill-agent-cli/README.md)
