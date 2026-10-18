# 前端与 Agent 接入验收

## 已完成

- 独立 HBuilderX uni-app Vue 3 工程，默认 C# API 8088、H5 5188；未改成 CLI/Vite 工程。
- 仅导入通用配置页面及组件；项目详情的私有业务子表已排除。当前自举20页中的菜单ID2200000000000099000映射树形工作台，旧菜单双表CRUD不发布。
- 页面直接使用 yzAdminTypeId / yzAdminTypeName 选择后台归属；工作台沿用 snapshot / revision / kind 外观，由后端单表存储。
- 运行时行为测试158项通过；旧Java模板逐字对照和旧元数据迁移脚本测试没有迁入，C#生成器的一致性由其自身测试负责。
- tools/metarill-agent-cli 本地测试19项通过，入口语法检查通过；保留 X-Yzp-Agent-* 协议头、请求预览、凭据隔离、拒绝重定向、不自动重试写请求和任务恢复提示。

## 尚未通过

- 用户选择保留原选择器、富文本交互，待确认原组件许可后再接入；未复制未确认源码和原私有API key，当前两类能力显式显示待接入。
- HBuilderX 5.26.2026091802 / CLI 1.0.0.0 已导入工程。publish web 实际报“appid 不存在，请在 manifest.json 中重新获取”；空值和本地占位不获接受，已恢复空值，不复用旧或他人标识。launch web --ui true --compile true 只返回命令已下发，logcat web 无构建日志，不能算编译成功。需正式新AppID后重新执行。
- 尚未完成C#真实后端全接口联调、浏览器实际业务闭环、微信/Android/iOS真机、原生签名、性能对照；Node模拟服务测试不能替代这些验收。
- Agent CLI client_id 为 metarill-agent-cli，服务端须支持设备授权、身份核对、能力目录、撤权、刷新和任务协议后才可宣布可用。

## 交付验收顺序

新AppID与插件许可→HBuilderX Web编译→登录/设备项目选择→元数据CRUD→结构预览/同步→生成与独立项目CRUD→菜单/权限/文件/导入导出/Agent→Web及三类真机→200服务器同资源性能对照。

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
| 字段配置新增/更新 | POST /admin/auto/config_column/6217715532627968/add 或 /update | JSON 为提交实体，parentId query（新字段归属 configTableId）；afterAdd 服务端给表的所有页面建 page_column |
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

运行时 getMenu 与工作台 tree 的模型不同：运行时节点使用 menu_id（兼容 id）、name、nodeType、children 及 target:{route,tableName,pageId,tableId,countEndpoint}。角标 isShowCount 生效时才 POST countEndpoint，载荷 {filters:{},parentId:''}。菜单导航查询参数 yzpMenuId 仅用于恢复选中，不能授予权限。

数据库动作目前经按钮网关映射：1696359699042168834→config_table/create_table，1696795154426523650→config_table/delete_table，1764267649526751234→config_column/add_column_to_db，1764274114228662273→config_column/drop_column_to_db，1696360770502291457→index/init_database。前端现有按钮没有结构差异预览及执行计划令牌界面；新“预览→执行→核验”流程需另行补齐界面与服务契约，不能将旧按钮直执行宣称成已满足。

来源证据：pages/custom/select_project.vue、common/yzp_runtime/menuRuntime.js、common/menuWorkbench.js、pages/yzp/config_table-detail-6191083005411328.vue、pages/yzp/config_column-detail-6217715532627968.vue、pages/custom/page_detail_layout.vue、pages/custom/config_page_columns.vue、components/yzp-file-library/yzp-file-library.vue，以及原平台 ConfigTableServiceImpl.afterAdd / ConfigColumnServiceImpl.afterAdd。本节只是实际调用对照，没有修改 Core 或 Runtime。

## 后台归属配套修正

依据当前 platform.json 按列名查得 config_table.yz_admin_type_id 列ID 2200000000000000087、config_page.yz_admin_type_id 列ID 2200000000000000104。表配置详情/列表及首页按后台类型展示，移除旧一级菜单选择页弹窗、静态导入及回调。候选接口统一为 POST /admin/auto/config_table/6191083005411328/findNameIdYzAdminType2200000000000000087，三个配置页面各使用自身pageId与 findNameIdYzAdminType2200000000000000104；请求体仍为 {formParameters}，候选仍为原选择器组件接口，没有替换交互。插件许可与新AppID缺口保持原状态，以上修正不等于H5或真实后端联调完成。
