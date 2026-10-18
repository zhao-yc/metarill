# 前端与 Agent 接入验收

当前状态以此处更新及docs/acceptance.md为准；后续分批记录保留实施时证据，旧AppID阻塞描述属于历史。

2026-10-18：用户提供AppID __UNI__CF80CAE后，HBuilderX已实际编译并导出Web成功；发行产物经静态服务完成真实登录、项目选择、菜单、配置表列表与详情回读。保留原交互及组件许可占位；完整写入闭环和多端仍待验收。

## 已完成

- 独立 HBuilderX uni-app Vue 3 工程，默认 C# API 8088、H5 5188；未改成 CLI/Vite 工程。
- 仅导入通用配置页面及组件；项目详情的私有业务子表已排除。当前自举20页中的菜单ID2200000000000099000映射树形工作台，旧菜单双表CRUD不发布。
- 页面直接使用 yzAdminTypeId / yzAdminTypeName 选择后台归属；工作台沿用 snapshot / revision / kind 外观，由后端单表存储。
- 运行时行为测试以协调者最新集成报告为准；旧Java模板逐字对照和旧元数据迁移脚本测试没有迁入，C#生成器的一致性由其自身测试负责。
- tools/metarill-agent-cli 本地测试19项通过，入口语法检查通过；保留 X-Yzp-Agent-* 协议头、请求预览、凭据隔离、拒绝重定向、不自动重试写请求和任务恢复提示。

## 尚未通过

- 用户选择保留原选择器、富文本交互，待确认原组件许可后再接入；未复制未确认源码和原私有API key，当前两类能力显式显示待接入。
- HBuilderX 5.26.2026091802 / CLI 1.0.0.0 已发行Web成功。launch web界面模式仅下发命令、直接CLI也未启动预览，本轮使用发行产物的本地静态服务验证；HBuilderX热更新预览仍未通过。
- C#核心接口与CLI已通过真实MySQL联调；完整接口覆盖、浏览器业务闭环、微信/Android/iOS真机、原生签名和性能对照仍待验收。Node行为测试不能替代这些验收。
- Agent CLI client_id 为 metarill-agent-cli；服务端设备授权、身份核对、能力目录、撤权、刷新和任务协议已通过真实CLI联调，浏览器授权交互仍待验收。

## 交付验收顺序

插件许可确认及原控件接入→元数据CRUD→结构预览/同步→生成与独立项目CRUD→菜单/权限/文件/导入导出/Agent→Web及三类真机→200服务器同资源性能对照。

HBuilderX 自带条件编译预处理器与 Vue SFC 编译器检查了 74 个项目 Vue 文件，脚本与模板描述语法零错误；这仍不替代完整 H5 构建。官方组件源码按原样保留，未做项目格式化。

## C# 后端兼容路径表

以下路径均相对 /api。除登录外，请求头为 loginStateId 与 URL 编码的 JSON sysParams（设备/项目选择）；服务端必须重新校验身份、归属和权限。

| 调用 | 方法与路径 | 请求与必要响应 |
| --- | --- | --- |
| 登录 | POST /admin/sys/yz_manager/login | form-urlencoded: username,password,captchaVerification；R.data 为用户，R.map.loginStateId 为令牌 |
| 登录验证配置 | GET /admin/sys/yz_manager/cap-config | R.data:{enabled,apiEndpoint}；禁用仍返回 enabled:false |
| 设备候选 | GET /admin/sys/index/findNameIdYzManagerDevices | R.data:[{value,text}]，ID 为字符串 |
| 项目候选 | GET /admin/sys/index/findNameIdYzProject | query yzManagerDevicesId；R.data:[{value,text}]；选择动作仅写本机 sysParams |
| 菜单与权限 | GET /admin/sys/yz_manager/getMenu | 特例：顶层 menu 数组、permissions 对象，不能只装入 data |
| 当前权限 | GET /admin/sys/yz_manager/getCurrentPermissions | R.data:{isSuper,pagePermissions:{页面ID:[动作]},buttonPermissions:[按钮ID]} |
| 表配置列表 | POST /admin/auto/config_table/6191083005411328/page | JSON:{orderBy,pageNo,pageSize,filters,parentId}；R.data:{records,total} |
| 表配置详情 | GET /admin/auto/config_table/6191083005411328/detail/{id} | 可带 parentId query；R.data 为完整实体 |
| 表配置新增/更新 | POST /admin/auto/config_table/6191083005411328/add 或 /update | JSON 为提交实体，R.data 必须返回完整保存实体与字符串 id；afterAdd 服务端创建默认页面 |
| 字段配置新增/更新 | POST /admin/custom/index/preview_batch_create_columns、/save_batch_create_columns；标准 /update 保留 | 原页 isNew=0，标准 /add 不开放；批量请求为表单或JSON:{config_table_id,content}，保存由同事务配置观察器创建全部页面字段 |
| 标准删除 | DELETE /admin/auto/{table}/{pageId} | JSON 含 id，可带 parentId query；权限和引用检查在服务端 |
| 自定义按钮 | POST /admin/sys/config_button/execute | JSON:{buttonId,entityId,entity?}，列表另带 visibleScheme/visibleRuleJson；以服务端配置为准，不能信任客户端规则 |
| 表级生成 | POST /admin/custom/config_table/generate_all?id={tableId} | 首页直接调用；生成目标现为 C#，不改现有按钮路径 |
| 页级生成 | POST /admin/custom/config_page/generate_all?id={pageId} | 同上；生成不等于部署 |
| 生成配置/运行时/全部页面 | GET /admin/custom/index/generate_project_config、/sync_runtime、/generate_all_page | 首页目前省略 method，实际为 GET；若改为 POST 须同步前端并记录兼容变更 |
| 菜单项目列表 | POST /admin/custom/menu_workbench/projects | {}→R.data:[{id,name,isV8}] |
| 菜单快照 | POST /admin/custom/menu_workbench/snapshot | {projectId,adminTypeId}→见下文 |
| 菜单保存 | POST /admin/custom/menu_workbench/save | {projectId,adminTypeId,revision,id,kind,parentId,name,icon,sortNo,hidden,targetPageId,showCount,visibleScheme}→完整新快照 |
| 菜单删除 | POST /admin/custom/menu_workbench/delete | {projectId,adminTypeId,revision,id}→完整新快照 |
| 详情布局快照 | POST /admin/custom/page_detail_layout/snapshot | {pageId}→R.data:{pageName,revision,detailLayout,fields} |
| 详情布局保存 | POST /admin/custom/page_detail_layout/save | {pageId,revision,detailLayout:{version:1,mode:none或tabs,groups:[{key,name}]},fields:[{pageColumnId,detailGroupKey}]}→新快照或至少新revision |
| 页面字段批量读取 | GET /admin/custom/index/config_page_columns?configPageId={id} | R.data 为页面字段数组 |
| 页面字段批量保存 | POST /admin/custom/index/save_config_page_columns | body 直接为数组，包含 id 与 isInGrid/isInEdit/isMust/isReadOnly/isInFilter/isImportant/isPrint 等标志 |
| 字段排序读取/保存 | GET /admin/custom/index/config_columns?configTableId={id}；POST /admin/custom/index/save_config_columns | 保存 body 直接为字段数组 |
| 文件列表 | POST /admin/sys/file-library/list | {configColumnId,keyword,folderId}；搜索时folderId:null，根目录为空字符串；R.data 为数组 |
| 文件访问解析 | POST /admin/sys/file-library/resolve | {fileIds:[字符串]}→R.data:[{fileId,originalName,url,previewUrl?,mode,mimeType,sizeBytes,folderId}] |
| 上传计划 | POST /admin/sys/file-library/upload-plan | {configColumnId,originalName,mimeType,sizeBytes,folderId}→{fileId,plan:{uploadMode,uploadUrl,headers,formData}} |
| 完成上传 | POST /admin/sys/file-library/complete | {fileId}→完整文件对象；SERVER_MULTIPART 上传使用 name:file 且附认证头，DIRECT_FORM 不附平台认证头 |
| 文件夹列表/创建 | POST /admin/sys/file-library/folders/list、/folders | 列表{configColumnId}；创建{configColumnId,parentId,name} |
| 文件夹更名/移动/删除 | PUT /admin/sys/file-library/folders/{id}、PUT .../{id}/move、DELETE .../{id} | 更名{name}；移动{targetParentId}；非空目录删除由后端拒绝 |
| 文件更名/移动/删除 | PUT /admin/sys/file-library/{id}/name、POST /admin/sys/file-library/move、DELETE /admin/sys/file-library/{id} | 更名{name}；移动{fileId,targetFolderId}；已被业务引用文件删除由后端拒绝 |

菜单工作台快照至少包含 projectId、adminTypeId、adminTypes、revision、isV8:true、canWrite、capabilities.first/second 的 create/update/delete 布尔值、tree 和 targets。tree 使用 id、parentId、firstMenuId、kind、name、children；targets 使用 id、kind、name、route、showCountSupported。根节点显示为 DIRECTORY，保存草稿里的 FIRST_MENU 是根分组语义；不要求底层保留两张表。冲突不能自动覆盖。

运行时 getMenu 与工作台 tree 的模型不同：运行时节点使用 menu_id（兼容 id）、text（组件展示）、name（兼容）、icon、projectId、adminTypeId、nodeType、children 及 target:{route,tableName,pageId,tableId,countEndpoint}。角标 isShowCount 生效时才 POST countEndpoint，载荷 {filters:{},parentId:''}。菜单导航查询参数 yzpMenuId 仅用于恢复选中，不能授予权限。

结构按钮现在由兼容网关解释：1696359699042168834（创建表）、1764267649526751234（创建字段）、1696360770502291457（初始化数据库）只返回 type:path 到 pages/custom/schema_sync，不能直接执行 DDL。1696795154426523650（删除表）和1764274114228662273（删除字段）明确拒绝。新预览页源码与状态机测试已补齐；真实 H5/多端验收仍受新 AppID 缺口阻塞，不能据单元测试宣称完整操作验收。

来源证据：pages/custom/select_project.vue、common/yzp_runtime/menuRuntime.js、common/menuWorkbench.js、pages/yzp/config_table-detail-6191083005411328.vue、pages/yzp/config_column-detail-6217715532627968.vue、pages/custom/page_detail_layout.vue、pages/custom/config_page_columns.vue、components/yzp-file-library/yzp-file-library.vue，以及原平台 ConfigTableServiceImpl.afterAdd / ConfigColumnServiceImpl.afterAdd。本节只是实际调用对照，没有修改 Core 或 Runtime。

## 后台归属配套修正

依据当前 platform.json 按列名查得 config_table.yz_admin_type_id 列ID 2200000000000000087、config_page.yz_admin_type_id 列ID 2200000000000000104。表配置详情/列表及首页按后台类型展示，移除旧一级菜单选择页弹窗、静态导入及回调。候选接口统一为 POST /admin/auto/config_table/6191083005411328/findNameIdYzAdminType2200000000000000087，三个配置页面各使用自身pageId与 findNameIdYzAdminType2200000000000000104；请求体仍为 {formParameters}，候选仍为原选择器组件接口，没有替换交互。插件许可与新AppID缺口保持原状态，以上修正不等于H5或真实后端联调完成。

## 严格 DTO 前端适配

生成器输出的 createFields/updateFields 是提交字段唯一来源。scripts/merge-page-write-schemas.mjs 确定性合并 platform 与 demo 的版本1清单，冲突或输入缺失时报错；当前21页清单两次合并SHA-256均为 e64e607baa93c1320208aa644ea41ae980ca963a3fc695eba50d244e458fdf9f。共享 yzp.request 在 uni.request 前只裁剪清单中标准 POST add/update 请求，保留自身字段的null/false/0/空字符串，缺失字段不补值；不修改调用者entity，不改变其他路由与方法。服务端白名单、认证和权限规则不变。新增7项清单/请求入口行为验证后，共165项前端测试通过；选择器、富文本许可与AppID/H5缺口仍保留。
## 元数据工作台服务端兼容

新增 MetadataWorkbenchBusiness 与独立 MetadataWorkbenchController。详情布局snapshot/save、页面字段批量读取/保存、表字段排序沿用原路由；两处bulk仍提交数组，通过 X-Metarill-Metadata-Revision 携带读取时 map.revision，前端save只序列化ID与允许标志/顺序，界面和第三方控件不变。服务端仅管理员入口，当前项目来自认证身份；先锁项目行，再锁页面/表/原列及页面字段，完整核验ID、同表/同页/同项目和revision后在同一事务更新。缺失标志保持不变，null标志拒绝；布局要求完整字段集合，显式空detailGroupKey回常显，null或缺失不隐式清空。

PageColumn加载同步 isInEdit→IsEdit、isInGrid→IsShow、isMust→IsRequired，保留原UI别名但不新增不存在的物理列。当前普通config_column页isNew=0仍保持关闭；新增列通过原preview_batch_create_columns与save_batch_create_columns专门配置领域入口，接受原form-urlencoded或JSON的 {config_table_id,content}，两列文本/JSON七字段白名单、500行/1MB与行错误协议沿用。保存锁内重检，重复字段或坏行整批拒绝；配置insert与ConfigurationMutationObserver的afterAdd同事务，成功明确返回database/generation:pending，不执行DDL或代码生成。另补workbench_columns读取已有字段列表。当前C#生成器尚未支持point/varbinary，预览会明确拒绝，不假称兼容。

验证：19项服务端单元+6项200隔离metarill_test真实集成测试，共25/25，无跳过；包括双连接同版本竞争、跨页/表整批拒绝、标志与Core同步、布局回读、原batch预览不写入、批量保存与两页afterAdd联动、重复保存重检。测试只使用随机项目ID并清理其记录。独立Controller与Runtime编译零警告；此分支前端160项测试通过，主分支叠加其他请求清单测试后以协调者计数为准。

集成需注册 AddScoped<MetadataWorkbenchBusiness>()、AddScoped<ConfigurationMutationObserver>()；保留原IRecordMutationObserver注册。测试项目 backend/tests/Metarill.Metadata.Tests，无隔离配置会明确跳过真实数据库项；CI应同时运行该项目。Repository别名归一仅在Read<T>局部，主分支新增ConfigCustomPages读取应保留。HBuilderX AppID、插件许可及真机未验收状态不变。

## 按钮兼容网关与结构预览

服务端新增 CompatibilityButtonBusiness 和 CompatibilityButtonController，协调者须注册 AddScoped<CompatibilityButtonBusiness>()。POST /admin/sys/config_button/execute 接收原 buttonId、entityId、entity，以及列表页 visibleScheme/visibleRuleJson 显示提示；后面三者不参与权限或动作判定。真实数据库 config_button 的 action_type=1、平台项目归属、发布配置页面和精确动作白名单必须同时匹配。实体及其关联表重新读库，核验当前项目；管理员也不能跨项目提交实体。sql 字段只进行路由字符串精确比对，禁止作为 SQL、脚本或动态地址执行。未知、删除结构和初始化账号均明确拒绝，不返回伪成功。

已映射原表/页的列排序、批量字段、页面字段、详情布局、配置表跳转与索引配置页。索引导航可用不代表其保存业务已移植；联接字段、数据库视图SQL、单独生成客户端、运行时同步和 Token 重置暂不支持。页面复制、创建页面与创建菜单的后续实现见下节。首页原表/页 generate_all 和导航直调接口也由同一网关处理，支持 query id、表单 id 和JSON id，冲突标识拒绝。生成返回 type:metarillGenerate、projectId；公共按钮处理器向当前API认证POST /admin/sys/generation/generate下载ZIP，错误JSON不能被当成ZIP保存。Web外平台明确提示暂未接入ZIP保存。

结构预览 POST /admin/sys/database/preview {projectId,tableId?} 显示服务器计划和完整SQL。确认后POST /admin/sys/database/apply {projectId,plan} 原样提交完整计划；客户端不编辑SQL，也不删除未知计划属性。一次确认只提交一次，成功或失败后再preview一次核验；核验计划不会自动执行。apply的R.data（失败时map.result或map.journal）作为执行记录显示。网络结果未知、部分DDL失败或核验仍有差异时，保留结果并要求手动重新预览，绝不自动重试。项目/登录变化和离开页面会阻止迟到响应继续执行。

专用路由声明于 frontend/metarill_uni_admin/scripts/custom-routes.json，在前端目录运行 npm run sync:custom-routes 确定性合并到 pages.json；每次重新生成路由后须运行此命令。脚本保持既有生成路由，重复运行changed:false，不能只手改pages.json。

本次验证：后端21项通过（含3项只读隔离MySQL网关/Controller测试，0跳过），并链接编译实际Controller；前端176项通过；75个项目Vue脚本通过H5条件预处理与SFC语法检查；别名导入缺失0。此处未执行DDL、未完成浏览器操作验收，未重新尝试无AppID构建。选择器/富文本继续保留现有设计与许可占位，不替换为picker或原生editor。

## 页面复制、从表创建页面与快捷创建菜单

新增 ConfigurationActionsBusiness，协调者须注册 AddScoped<ConfigurationActionsBusiness>()。原 copy_page、create_page、create_menu 按钮及已登记旧接口别名进入该领域入口，不能由客户端替换执行路由。所有动作要求管理员和真实当前项目，按项目→表/页聚合取得数据库锁；元数据新增、默认字段及审计写入使用同一事务，发生中途失败整体回滚。联接字段、数据库视图SQL和初始化账号继续明确拒绝。

copy_page 按原 Java ConfigPageController 复制页面、页面字段、按钮和原页面出向的 config_child；仅 parent_config_page_id 改为副本，child_config_page_id 仍是原目标（包括自引用原页），不复制入向关系或角色权限。主键及审计重建，保留原详情布局和分组key，并返回原页面字段ID→新ID映射；每个字段必须引用同表列，父页、联接页/表及子页关联必须属于当前项目。

config_table/create_page 的原按钮实际创建页面配置，不创建数据库视图。新页沿用表所属后台和既有CRUD默认配置，复用 ConfigurationMutationObserver 在事务中补齐当前页字段；已有字段不重复。系统字段默认只读、联接派生字段只读，sort_no沿用默认99及必填规则。观察器对config_page限定当前页的修正由根集成，表/字段新增仍补全部关联页。

create_menu 使用页面直接归属后台，在该后台根目录“页面”下增加标准页面入口；没有该根目录时同事务创建，同后台有多个匹配根目录则拒绝，页面已存在导航时幂等返回。父链、后台、目标、深度和环校验复用 MenuWorkbenchBusiness.Validate，版本复用其Revision并按落库后快照返回。为避免锁顺序反转，不在已持有项目数据库锁后嵌套调用菜单Save的进程gate；所有菜单新增仍在同项目数据库锁与同事务内。子表页面不创建独立入口，不自动授予任何角色权限。

成功返回 type:metarillRefresh 与配置回执，共享按钮处理器沿原详情/列表刷新和自动关闭设置处理，不把回执覆盖到entity；菜单动作发送既有onMenuConfigurationChanged事件。选择器、富文本及多端操作设计没有替换，H5 AppID和许可验收缺口不变。

本批验证：兼容网关/领域动作后端29项通过，包含8项随机独占项目的真实MySQL动作测试及原3项只读网关/Controller测试，0跳过；前端182项通过，新增4项实际公共按钮处理器测试覆盖实体不被回执污染、菜单刷新、自动关闭与原实体响应兼容。执行中途ID生成失败能回滚已插入页面与字段；并发菜单创建验证使用两条独立数据库连接。未运行H5或设备操作验收。
