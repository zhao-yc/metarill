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
