# 独立应用包与前端宿主

独立包将项目元数据、生成强类型后端、Core/Data/Exports/Files 源码及 ASP.NET Core 宿主放入同一目录。普通 CRUD 和文件、导出不依赖在线 Metarill 平台。MySQL 结构仍从元数据显式预览、执行和核验，普通启动不执行 DDL。

## 两种显式模式

CLI 默认 `FullApplication`，包含 `frontend` HBuilderX uni-app Vue3 工程；`--backend-only` 明确选择 `BackendOnly`，保留后端与生成页面片段。完整模式遇到未接线的 CustomControl 或自定义/外部菜单时，在写 stage 前中文拒绝并指出页面或控件，不自动降级。

```powershell
dotnet run --project backend/src/Metarill.Cli -- package --metadata backend/metadata/demo.json --output .local/demo-application --namespace Example.Demo --source-root .
# 平台配置前端包含专用页面与组件，需单独提供；显式保留仅后端包：
dotnet run --project backend/src/Metarill.Cli -- package --metadata backend/metadata/platform.json --output .local/platform-backend --namespace Example.Platform --source-root . --backend-only
```

平台生成下载 API 根据服务端已发布平台项目身份显式选择 BackendOnly，业务项目选择 FullApplication。ZIP 的 `.metarill-package.json`、README 与响应头 `X-Metarill-Package-Mode`、`X-Metarill-Frontend-Included`、`X-Metarill-Package-Warnings` 均记录模式；warnings 头为 URI 编码的 JSON 字符串数组。平台行为不由客户端另传模式或路径决定。

## 独立前端边界

前端宿主保留登录表单、菜单展开/导航、请求 loading、错误提示及标准 list/detail 的交互，复用公共请求运行时、菜单状态机、FileLibrary、日期选择、搜索多选、Tiny Cloud 与 DCloud uni-ui。生成页面与页面写入 schema 只使用当前业务项目元数据。`pages.generated.json` 组装到独立宿主 `pages.json`，不复制平台配置页面或平台 Agent 入口。

源资产来自固定逐文件 allowlist，所有祖先路径拒绝符号链接和 junction；新出现的私有文件不会自动进入包。只生成公开的项目 ID/名称/后台 ID；源平台 environment、AppID、`.local` 和 appsettings.local 不复制。依赖锁文件只允许核对后的 npm registry 和带完整性校验的离线 SheetJS 归档，许可证随包保留。元数据敏感键先脱敏，非标准键仍需发布者审核。

生成前端默认 DCloud AppID 与小程序 AppID 为空。开发者须自行配置 `frontend/common/application.config.js` 的 API 地址，安装锁定依赖，在 HBuilderX 填写自己项目的 AppID。分离端口与真机上传还需设置后端可信 `Files:LegacyPublicBaseUrl`，不从请求 Host 推断公开地址。具体操作见包内 README。

## 开发认证

仅 Development 环境且显式启用 `Authentication:DevelopmentBootstrap` 才能使用开发账号；用户名、至少12位的密码由开发者私有配置，没有默认真实账号密码。服务端固定包内单项目，签发内存不透明会话，每次 API 验证；客户端项目选择和 query 令牌不产生身份。菜单与权限查询也受相同认证约束。

独立宿主仅在 Development 且显式启用开发认证时允许 HTTP(S) 回环 Origin 的本机跨端口预检；不允许外域或带路径、用户信息、query、fragment 的 Origin，也不设置 AllowCredentials。CORS 在认证前执行，因此真实预检可通过，匿名业务写仍返回401。关闭开发认证或 Production 不默认开放；其他Web来源需可信同源网关或自有部署策略。

这不是生产账号系统。正式项目须在手写 partial `HostCustomization.ConfigureServices` 中接入自己的 `IRuntimeAuthorization`，不能把开发管理员直接作为生产认证。

## 重新生成与验收

包输出目录必须为空，已有手写宿主、API 配置或生成工程不覆盖。再次导出到新空目录后审查差异，保留手写文件；后端 `.g.cs` 与 `.cs` 的 partial 规则继续适用。相同输入、相同公共源资产得到相同包字节，清单记录 SHA256。

`npm test` 在 Node 24 检查 H5/Vue3 条件编译后的真实 JavaScript 语法、ES/CommonJS 导入、生成路由及组件文件闭包。真实隔离 MySQL 测试从打包结果构建宿主、显式结构同步，执行生成登录/菜单/list/detail 脚本到实际 HTTP 接口，并验证新增、显式 null、读取、删除及注销失效；这仍不是浏览器 DOM、HBuilderX 发行、小程序、Android 或 iOS 验收。Excel/DOM 第三方组件只在此脚本中检查依赖闭包，不声称已在 Node 模拟完整执行。

旧 ImageUpload/RTF URL 沿用公开上传协议，尚不具备 FileLibrary 字段的引用阻删生命周期。Agent、复杂自定义 SQL、平台设计器和正式账号体系不在当前独立前端宿主范围内。

## 独立生成包 Web 与主子浏览器验收（2026-10-19）

协调者从元数据和公共源重新输出全新私有目录，装配 450 文件 FullApplication 包；导出默认 AppID 为空，只在验收副本填写用户已提供的 Metarill AppID 和回环 API 地址。`npm ci --ignore-scripts --offline`、随包 `npm test`、后端真实构建（0警告/0错误）均通过，HBuilderX 5.26 Vue3 的实际命令为：

```powershell
D:/HBuilderX/cli.exe publish web --project E:/git/metarill/.local/generated-ui-8c394058-v2/frontend --webTitle Metarill独立应用验收 --webHosting false
```

产物静态服务 5194 与独立后端 8098 分离端口，浏览器使用真实开发登录和发布菜单进入新生成页面。父记录保存前不挂载子表；保存后显示 Tab 子列表。纯界面完成父新增、PC 弹层中两条子记录新增/修改/软删除、名称筛选和重置、升序/降序回读、父备注最终保存及列表回读、父软删除和注销。子记录操作前故意修改父备注而不保存，子列表刷新后该脏字段仍保留；重新从列表进入父详情确认最终值真实持久化。父只读路由下父字段禁用，父保存/复制/删除及子新增隐藏；该前端行为不替代独立的服务端权限测试。

结构验收先证明随机子表不存在，完整预览仅一条 CREATE TABLE，持久化创建意图后显式执行并核验0差异。浏览器退出后先停本轮进程，再核对精确父ID、最终备注/金额和两条子记录的归属及软删除状态，只清理本轮随机表与父行；不存在核验及清理回执保留于私有 `.local`。两个临时开发认证配置已删除，其余既有数据未清理。

本批只验证两条子记录与 1/1 分页状态，未验证浏览器跨页边界或超过5000候选；后者已有真实 HTTP 回归，不能互相替代。小程序、Android、iOS、真实 Tiny Cloud、文件/导入导出的独立包浏览器操作和正式账号体系仍分别待验收。Tiny Cloud key按用户要求保持空值。独立8098/5194临时服务已停止。

## 跨工作副本文本规范化

受控前端文本按严格UTF-8读取、移除UTF-8 BOM并统一LF；字体和离线tgz按原字节复制。历史西语JSON已按原GB18030严格解码转存UTF-8，文案未改。真实CRLF/BOM源码副本与LF源码生成的全前端文件及清单相同，开发者改行尾后随包Node校验也可执行；未闭合条件块明确拒绝。15项前端打包回归及10项应用包/真实MySQL文件链定向回归已通过，独立克隆此前失败日志保留，不据主工作副本偶然LF把失败删除。

## 独立宿主只读结构反查

在独立包根目录运行 `dotnet run --project backend/Application.Host -- --schema-inspect`。该命令固定读取包内 `project.json` 与 `files.json` 合并的 physical metadata，以及部署者的私有 `Database` 配置，复用 `SchemaService.InspectSnapshotAsync`。标准输出是单一 JSON：`executable:false`、`provider`、`databaseSchema` 和 `fingerprint`；`databaseSchema` 包含实际受管表、列、索引与 SHOW CREATE 快照，不包含业务行数据。

命令不创建执行计划、不保存元数据或执行历史、不执行 DDL，也不启动 HTTP/后台服务。普通 Web 启动不因该能力读取或修改结构。`--schema-inspect`、`--schema-preview`、`--schema-apply` 互斥，不可重复；混用、赋值变体或未知 `--schema-*` 参数在构造宿主前明确拒绝。预期库和连接实际目标不一致时拒绝，不回退到其它数据库。

这是独立包本地管理员对固定受管模型的只读命令，不是 Metarill 平台 CLI 选项目，不提供未登记表目录、元数据反向导入或任意 SQL 入口；不会把快照当可执行计划。当前仍只有 MySQL 实现，顺序快照不保证外部并发 DDL 下的一致时刻，后续变更前须重新核验指纹。
