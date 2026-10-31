# 数据库结构只读反查

平台提供 `POST /api/admin/sys/database/inspect`，请求为 `{}` 或 `{"tableId":"当前项目配置表ID"}`。MCP 工具 `inspect_database_schema` 使用 `data` 参数接收同样对象。接口要求真实网页登录管理员；项目由认证上下文选择，客户端不能传项目、连接串、物理表名或 SQL。未知/重复键、无效或越项目 ID、显式 null 均拒绝。

响应包含当前项目、适配器读取的库/表/字段/索引/SHOW CREATE 快照、结构指纹、受管及缺失表名，以及被排除的非实体定义。功能表/视图不列入 missingTables；项目级请求在 excludedTables 说明原因，单独选择它们则明确拒绝。完整项目范围组合由平台元数据派生的同库文件基础表；单表范围仅返回所选有效定义。数据库适配层检查实际连接库与预期一致。响应始终 `executable: false`，没有执行计划 ID，读取不会新增执行记录、写入元数据或执行 DDL。SQL 定义是只读快照，执行仍必须走原显式 preview → apply → 核验链路。

这是顺序读取的结构快照，外部并发 DDL 可能改变后续结果；反向导入和执行前需要重新反查及核对指纹。原 inspect 接口仍只覆盖已登记受管表；新增物理目录见下文。未登记实体表的逻辑草案及显式确认配置保存已接A11固定子集；视图/未知类型导入、完整归属/生成矩阵仍未完成，不能据此声称全库逆向导入。首期适配仍为 MySQL，其他数据库须完成对应适配和验收。

验收使用 Server-200 隔离业务库验证 HTTP/MCP 指纹一致、文件基础表组合、单表范围、匿名/非管理员/Agent/跨项目拒绝及未知SQL/重复键拒绝。快照前后执行历史不变，重复只读指纹相同。缺少私有测试配置的 CI 场景明确跳过；数据库真实性以协调者实际执行的 TRX 为准。

## 独立包的固定受管结构命令

在独立包根目录运行 `dotnet run --project backend/Application.Host -- --schema-inspect`。该命令固定读取包内 `project.json` 与 `files.json` 合并的 physical metadata，以及部署者的私有 `Database` 配置，复用 `SchemaService.InspectSnapshotAsync`。标准输出是单一 JSON：`executable:false`、`provider`、`databaseSchema` 和 `fingerprint`；`databaseSchema` 包含实际受管表、列、索引与 SHOW CREATE 快照，不包含业务行数据。

命令不创建执行计划、不保存元数据或执行历史、不执行 DDL，也不启动 HTTP/后台服务。普通 Web 启动不因该能力读取或修改结构。`--schema-inspect`、`--schema-preview`、`--schema-apply` 互斥，不可重复；混用、赋值变体或未知 `--schema-*` 参数在构造宿主前明确拒绝。预期库和连接实际目标不一致时拒绝，不回退到其它数据库。

这是独立包本地管理员对固定受管模型的只读命令，不是 Metarill 平台 CLI 选项目，不提供未登记表目录、元数据反向导入或任意 SQL 入口；不会把快照当可执行计划。当前仍只有 MySQL 实现，顺序快照不保证外部并发 DDL 下的一致时刻，后续变更前须重新核验指纹。

2026-10-19 独立命令定向回归 2/2、0 跳过：无数据库配置的真实子进程覆盖三命令重复/混合、赋值和未知参数拒绝；Server-200 隔离测试使用元数据创建自己的随机表，经真实生成 HTTP 新增后停止服务，再执行只读命令，核对实际快照与指纹、完整行摘要、包内元数据和已有 journal 字节不变。缺表时反查不创建表，错误目标拒绝；固定文件表只反查复用，测试仅清理自身随机表和临时包。首次 Windows 中文错误输出编码匹配失败的记录保留，固定 UTF8 后实际复验通过，未把失败或跳过计作成功。

原有独立包真实 MySQL 文件/CRUD 用例另定向 1/1 通过，继续覆盖正常 Web 启动不建表、包内 --schema-preview / --schema-apply、零差异核验和既有文件事务链路；不把这一单用例重复运行叠加为新功能用例。


## 平台 CLI 与未登记物理对象目录

平台 CLI 新增两个在线只读命令，使用现有网页登录的 `loginStateId`，不使用 Agent Bearer、不建立数据库直连、不保存或刷新会话。先从本机私有配置或密钥管理工具向环境注入 `METARILL_LOGIN_STATE_ID`，避免把会话值放进命令行、源码或文档。可用 `--token-env 变量名` 指定另一个环境变量；会话到期后须按原登录流程取得新会话。

```powershell
# 当前项目的已登记受管结构，复用原inspect接口。
dotnet run --project backend/src/Metarill.Cli -- schema inspect --server https://你的平台域名/api --project-id 项目ID
# 仅反查一张当前项目已登记表；table-id是元数据ID，不是物理表名。
dotnet run --project backend/src/Metarill.Cli -- schema inspect --server https://你的平台域名/api --project-id 项目ID --table-id 受管表ID
# 当前项目服务端数据库的物理目录；不会自动导入元数据。
dotnet run --project backend/src/Metarill.Cli -- schema catalog --server https://你的平台域名/api --project-id 项目ID
# 用毕可清除当前终端的会话环境变量。
Remove-Item Env:METARILL_LOGIN_STATE_ID
```

`--project-id` 仅经原 `sysParams` 头选择项目，服务器逐请求重新认证管理员及项目访问；选择不等于授权。`schema inspect` 的可选 `--table-id` 进入原请求体。目录命令不能传 `--table-id`。未知/重复/缺值参数、非规范整数、直接 token、SQL和连接配置均拒绝。远程地址须为 HTTPS，仅 localhost/回环 IP 允许 HTTP；地址路径仅接受 `/`、`/api` 或 `/api/`，不接受 URL 内凭据、查询串和片段。HTTP 不跟随重定向，不使用 Cookie，响应最多8 MiB，客户端超时30秒。HTTP 200 中的原 R 错误同样失败；跨项目或 executable 非 false 的响应拒绝，标准输出为空，标准错误不回显服务端正文、会话或异常链。中文及JSON输出统一UTF8。

新接口 `POST /api/admin/sys/database/catalog` 只接受 `{}`，MCP `inspect_database_catalog` 的 `data` 使用同样对象。二者复用受管反查的真实网页登录平台管理员边界，匿名、普通账号及 Agent 身份不允许使用。当前项目数据库只能来自服务端已配置连接，不允许请求库名、表名或任意 SQL。普通业务原页面和原 inspect 合同保持。

返回 `projectId`、`provider`、`database`、`objects`、`catalogFingerprint`、`caseSensitiveNames` 和固定的 `executable:false`。每个对象只包含物理名称、类型、引擎、`supportedIdentifier` 及 `registeredInCurrentProject`：

- 目录可见范围是平台管理员当前选中项目所连数据库中的表/视图；共享库可能显示其他项目的物理名称，不读取其业务行、字段或DDL正文。
- 登记状态包含当前项目的同库文件基础表；功能表不冒充实体。名称比较依据适配器读取的 MySQL `lower_case_table_names`，不用平台运行系统推断。
- `registeredInCurrentProject:false` 只表示本项目没有对应有效定义，不能证明对象无其他归属、可以导入或可以覆盖；跨项目同库归属确认由后续导入流程单独处理。
- 旧表名称不符合平台标识符规则时仍可在目录中识别，`supportedIdentifier:false`；符合名称规则也不代表其类型、引擎、主键或关系已支持导入。视图仅列目录，不在此读取或导入定义。
- 最多4096个物理对象；第4097个触发明确失败，不将截断目录当完整结果。此预算与客户端8 MiB响应预算均独立生效。
- 目录指纹覆盖数据库名、名称比较规则、对象名称/类型/引擎；不覆盖业务行、字段、索引、元数据登记状态或授权，也不替代原结构指纹和DDL执行前复核。

目录查询不创建执行计划或历史，不创建/变更表，也不写入元数据。A11已有结构→逻辑草案→审核→明确确认→元数据事务保存固定子集；不能把目录结果直接提交给结构执行或当作可导入授权。外部并发DDL时目录可能变化，后续操作应重新核对。

### 本轮验证记录

2026-10-28完成A10固定MySQL范围：元数据全量328/328、API全量87/87、原本地更新／包兼容74/74，均零失败零跳过。真实CLI子进程连接Kestrel平台及Server-200业务测试库，实际完成catalog/inspect、非法会话／无效项目拒绝；真实默认传输拒绝重定向，不转发会话或打印服务端错误，响应头后持续不结束的流也在总期限内失败。无Content-Length的8 MiB超量流由专用夹具验证逐块读取预算，不能用MemoryStream的可计算长度替代该分支。

目录实库覆盖已登记普通表与文件基础表、未登记表、视图及不符合标识符规则的旧名称；HTTP／MCP／CLI目录指纹一致。原受管反查不混入未登记表，当前项目有效配置及结构执行历史不变。最终两张独占随机表和一个视图均在finally清理，并以独立数据库连接确认剩余0。没有创建额外数据库／账号、授权或执行真实元数据导入。没有为验证4097对象上限创建数千张表，也未切换MySQL名称大小写模式；这些实现边界不能冒充相应大库／配置矩阵已实测。

两轮正式生成215文件（含24手写）稳定，原前端／模板／元数据546文件逐字保持。最终API及CLI严格Release构建0警告0错误。原首页三个按钮、页面、样式和交互未变；测试覆盖其本地更新底层协议，不代替完整浏览器操作验收。本轮未新增浏览器、设备、多数据库或同条件性能结论，A11与MySQL首版完整验收仍未完成。

私有证据位于 `.local/platform-schema-cli`：API/更新固定来源 `b5cfc1c5471e`（1530文件）；随后只加强无长度头测试夹具，元数据328项再次通过。两轮产品源1051文件完全相同，摘要 `a7dc58d02a9ce8fdcc29da4397a37adf34787e286260e1ad79b4c7d05b9cfc1b`。各轮TRX、来源快照、独占对象意图及清理、验证回执分别保留，不累加试跑为覆盖。初次Windows输出编码失败、带点新文件名被原更新白名单拒绝的两项失败均保留；通过UTF8输出及改名SchemaCatalog.cs修正，没有放宽原更新路径规则。

## 部署者跟进

没有数据库结构、元数据或旧数据迁移。平台后端按既有部署流程更新并重启，CLI需重新编译；MCP客户端重新发现工具即可。已有独立应用可继续使用原固定 `--schema-inspect`，本次平台CLI不要求修改原页面。下发同版基础设施时沿原 `sync-runtime` 同步Data源文件，不能只复制新增partial文件而遗漏SchemaService的partial声明。此次仅提交源码与文档，未部署生产或合入main。


## A11后续：未登记表的只读逻辑草案

`schema import-preview`、HTTP `/api/admin/sys/database/import/preview` 和MCP `preview_database_metadata_import` 已补齐源结构到逻辑草案的第一阶段；固定canImport:false，符号ID不能保存。确认保存另用prepare/apply/status，不改变该只读草案合同。具体持锁、类型缺口、实际验证及尚缺的完整归属/生成矩阵见[反向逻辑草案](schema-import-draft.md)。这不改变A10的受管inspect/catalog合同，也不代表A11已完成。
