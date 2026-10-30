# 独立管理模块同源装配（2026-10-29）

本批继续C05，并以C20记录装配准备子集。不是新增一套管理界面：原三份安全Service partial、原角色权限工作台/组件、普通完整包的基础源码、生产Program、Web认证、Agent与MCP共同装配，原DOM、样式、交互、按钮和HTTP接口不改。

## 装配来源与隔离

`SecurityManagementModuleFactory` 私有保存签发时的原业务目录；只能由真正签发对象取独立副本，伪造同形Binding或后改原输入/副本不能替换来源。内部 `SecurityManagementApplicationPreparer` 先调用普通完整包路径完成脱敏及基础闭包，再根据脱敏目录和固定后台重新签发管理模块，生成真实管理实体/DTO/Mapper/Service/Controller与三份安全手写扩展，加入原工作台及Agent两页。

基础工程、具体依赖版本、许可证、源码/资源链接守卫和全部生产宿主模板继续复用 `ApplicationPackager`。准备结果不引用主仓库绝对ProjectReference；不以手工复制几份Controller模拟完整运行时。已存在的手写内容不在再次生成时覆盖。

发布目录与编译安全摘要从实际脱敏后目录同步重算；初始每个文件有SHA256。两次同输入准备的全部文件逐字一致。原工作台页、两权限组件、permissionWorkbench.js及Agent两页与既有原源码只统一换行后相同。

## 完整管理发布及正式初始化验收门槛保留

本批只提供内部同源准备入口，没有新增CLI参数、HTTP入口或自动选择规则。普通Full/Create路径仍拒绝安全CRUD伪装及未接线Custom；管理准备包不含普通 `.metarill-package.json`，改用 `.metarill-management-preparation.json`，明确 `readyForPublication=false`，同时保留全部RequiredBackendAdapters与BackendGaps。README首行也标记尚未发布。不能将准备结果直接当作普通Full候选或完整可用应用交付。

C20准备时C06规则仍待确认、未注册HTTP或DI。2026-10-29用户已确认保留账号ID规则，C41将原中央按钮和直调URL接至可信同库初始化Business及DI；原按钮、页面和确认交互保留。C05仍须完成正式对外打包/更新路径及同包真实身份/数据库/工作台验收。本批没有DDL、元数据迁移、原前端修改或生产部署，亦不修改C19冻结包或消费其待授权租约。

## 实际验证与限制

新专项3项零跳过通过：准备包自身源码独立严格构建、原资源/脱敏/逐文件摘要、三份partial、重复准备/手写保护、伪造来源拒绝及发布故障保留。包内只读HostCustomization观察器未替换认证、Controller、权限或数据库类型；用Production/Web、禁用开发Bootstrap、不可达回环端口1的数据库配置启动实际Program，真实解析三张安全Service及唯一首位协调器，确认授权与身份访问器为同一Scoped实例、工作台/MCP存在、平台身份域为false；三管理页及初始化路径的匿名请求均为401。没有调用SQL、登录、初始化或管理写入；这些不算实库/浏览器/正式模块验收。

发布失败保留本轮真实完整stage及外层诊断；其余失败只清理本轮新建同父随机目录，不覆盖已有非空输出。新工作台资源与Agent开关只在内部受信准备路径合并，普通前端包的默认行为不变。

首轮严格编译因测试误用MetadataCatalog.Extra及xUnit单例过滤写法失败，已修正；后续HTTP测试先后修正HttpMethod命名空间和未预建日志目录，保留所有失败记录，未放宽原日志守卫。实际观察回执在任务克隆 `.local/management-preparation-http`，TRX/日志在主目录私有 `.local/management-package-preparation-20261029`。

## 最终回归

| 验证 | 实际结果 |
| --- | --- |
| 新同源装配专项 | 3通过，零跳过；包含真实独立构建/生产宿主DI/匿名HTTP及重复逐字稳定 |
| 相关完整包、原前端、工厂、三Service、物理图、工作台及管理Program | 127通过，零跳过；包含上述3项，不能相加 |
| CLI严格构建 | 零警告、零错误 |
| 普通平台/示例两轮正式生成 | 705文件逐字稳定、490原前端及24手写partial不变 |

冻结起点为 `3ea948d54dc10f6543c9132720e2add2e415fef8`，实现、测试及进度记录同提交。API/Data/Security/Agent全量本批未重新执行；本批改变Generation内部准备/工厂及前端资产装配，实际使用同源Core等运行时的独立构建和回环匿名仅证明上述范围，没有新增实库、浏览器、设备或性能结论。

## 跟进方式

本批不要求既有项目改表、回填或再生成，也不会自动修改旧工程。普通Full项目继续原路径；后续获确认并正式开放管理模块时，要成套下发Core/Data/Security/Agent/MCP、原宿主与已签发元数据/三份partial/工作台，复核初始化语义及受管更新，不能仅复制一份DLL或在业务JSON填trusted标记。C05、C06与MySQL首版整体继续保持未完成状态。

## C41 同库初始化接线（2026-10-29）

用户已确认保留账号ID、拒绝跨后台或墓碑同名、重置密码/角色并撤销旧Web会话。生成的可信管理模块现在显式注册初始化Business，并生成原直调路由；生产中央按钮模板也转入同一共享边界。普通模型与普通Full包不注册此接口，也不生成该Controller。完整管理包仍保留准备标识及发布门槛，C07平台到其他业务库未接通，不能将本片当作完整初始化或正式实库验收。验证细节见[初始化HTTP接线](testing/admin-initialization-http.md)。

## C49 实际基础物理图核对（2026-10-30）

管理装配现在核对实际候选中的files/security完整表集合及字段、索引物理定义，预期来自工厂已签发模块的固定副本。新增、遗漏整表和同名表内的字段/索引错配均在管理再生成及发布前拒绝；文案或JSON排列保持可变。真实错配红灯与最终28项回归、严格构建证据见[基础图一致性](testing/management-foundation-closure.md)。普通Full入口、原前端及准备包的未发布标识保持，C05完整管理发布/更新和正式矩阵仍待完成。

## C50 原业务来源与独立进程重建（2026-10-30）

准备包将普通完整包已脱敏的原业务元数据单独保存到 `backend/metadata/application-source.json`，登记字节摘要，发布前实际落盘回读、按当前固定工厂重新签发。新的生成器进程可以不依赖旧签发对象再生成，手写partial继续保留；相关44项零跳过与严格构建通过。旧准备包缺来源时需重新准备，不反推合并目录，也不改旧冻结包。具体来源/图摘要/命名空间/归属核验和兼容证据见[原业务恢复](testing/management-source-roundtrip.md)。本批不是公开Full发布或完整更新器接线，C05正式矩阵仍待。

## C51 显式完整管理包（2026-10-30）

新增 `CreateManagement` 与CLI `--management-admin-type`，明确选择原业务已发布后台，生成独立模式 `FullManagementApplication`。原准备路径仍保留未发布状态；新路径将来源独立为五份固定元数据的通用声明，外层完整清单绑定实际模块/后台/来源，成套提供当前源码、原三管理页/工作台和Web/Agent/MCP。已绑定实际受守卫模板时不再把“验收待做”当成“协调未接线”，普通模型原门禁保留；RequiredBackendAdapters继续表示四项实际依赖。

完整包自身源码严格构建、Production/Web匿名边界、真实CLI、旧普通更新/回滚等133项通过；最终来源/管理/基础/工厂45项复验通过，严格零警告错误，普通生成两轮不变。现有更新器明确拒绝新模式，C05受管更新与正式同包矩阵仍须继续，不以这些验证宣告完整发布。入口、限制和原失败记录见[完整管理打包](testing/management-full-package.md)。
