# 前端沿用要求与实现纠偏

## 用户确认的目标

主要替换后端为 C#，维持现有 YZP 主框架和前端。前端继续使用现有 uni-app 工程、原 PC/移动端页面、组件、布局和交互；仅做 C# 接口及已确认的单表菜单/页面归属调整所需的适配。不能将复用 uni-app 或组件等同于沿用前端，也不能以验收样板为理由另建正式页面方案。

## 已确认的实现偏差

平台管理端 pages/yzp 大部分沿用原页，但 C# 的 UniappRenderer 使用了新写的 list.vue.tpl 和 detail.vue.tpl，所有普通生成业务页均走该新模板，独立包又装配了简化宿主。用户截图中的关系验收页正来自该通用生成链路，布局变化不只是测试数据造成。

原列表包含 list-page-pc/list-page-mobile、pc-filters、原按钮和操作列、uni-pagination、移动端筛选弹层与 uni-load-more；新模板使用单一表格及上一页/下一页按钮，未完整沿用这些结构。原详情、导航壳和复杂组件交互也须逐项核对，不能只补同名 CSS 后宣布保真完成。

## 纠正顺序

1. 冻结并只读核对原 YZP 前端与 v8 生成源头，保留来源和摘要；不修改旧仓库或覆盖他人工作。
2. 从原页面及模板恢复完整生成结构、脚本和 PC/移动端分支。C# 生成器负责相同元数据的展开，不能把新的简化模板保留为默认最终方案。
3. 对照原页面请求核实 C# API 兼容，优先由后端边界适配；前端仅做必要的请求、配置及已确认菜单变化。
4. 用相同元数据对照生成原页与 C# 页，实际检查 PC 和移动布局、筛选/分页、按钮、表单/校验、主子、关系、文件和导入导出，再在浏览器执行，不以结构断言或构建代替。

当前是纠偏进行中，尚未完成原前端保真验收。已有 MySQL、事务、生成保护和新页面浏览器结果仍保留为其实际范围证据；它们不能证明原前端已沿用，也不能据此满足本项目的前端交付条件。暂停扩展基于简化布局的样板，已授权在线助手继续只接原首页三个按钮，不改首页布局。


## 2026-10-20 原列表恢复切片

列表默认渲染已切到 `OriginalUniappListRenderer`，模板通过 `tools/generation/extract-original-uni-list.py` 从原 `UniappService.GenerateList` 的字符串逐分支提取，来源与SHA-256保存在 `backend/templates/uniapp/original-list-source.json`。提取器只解释字符串拼接和固定布尔分支，不编译/执行旧Java，不访问数据库。五份原模板按UTF-8/LF保存。

保留原PC表格/文字操作列、三种移动卡片、筛选弹窗、原分页/加载更多、原顶部与行内按钮、批量勾选、原子表详情弹窗、下拉控件和导入导出请求。多选选择页只在内部桥接完整同权回读和请求隔离，可见工具栏与事件仍按原 `selectedList`、`onPopSelect(row[])` 和 `onPopClose`；仅看已选清空后保持空结果，不将空ID条件送成查询全部。Vue3残留 `$set` 仅改为等价的响应式属性赋值。

本切片实测：生成库Release编译0警告/0错误；原模板提取核对通过；真实生成Vue的编译与挂载4/4通过；公共前端232/232通过、0跳过。最初探针缺少popup公开方法，3项失败已保留日志；补齐测试宿主后复测通过，未修改生产页面绕过。

尚未完成：详情/导航壳切片整合、原HTTP边界兼容整合、旧简化页面专用测试改为原合同、完整再生成、HBuilderX构建和原版浏览器逐页对照。当前4项探针不是浏览器或跨端验收，不能据此宣称前端恢复已完成。日期/关系/文件及自定义控件的完整后端缺口继续受既有能力清单与导出门禁约束。


## 默认入口整合与审查修正

默认UniappRenderer已同时调用原列表与原详情，移除旧简化列表、详情及弹窗模板，避免维护两套默认页面。原模板DOM保留；修正旧列表“isUpdate=0也隐藏新增”的问题，列表只承接父/全局只读，新增和详情更新各自校验能力与权限。多选确认重试先核验原selectedIds，非法JSON、0、溢出/不安全数字不能被重置成合法空数组再保存。

主分支整合后的原列表、原详情和旧实体归一规则24/24通过、0跳过；包括真实Vue3编译/挂载及上述反例。C#实际HTTP、Agent严格DTO目录、MySQL事务的独立切片证据见original-page-api-compatibility.md；尚需在主分支完整回归，不能合并计成浏览器实测。完整旧UI专用tests/probes正改为原协议；既有主子/文件/关系安全检查仍须保留。


原数组下拉框仍使用原组件及固定findNameId路径；子表候选仅补实际parentId必要参数。`originalListOptionsMixin`在内部隔离换父、换身份、同端点后发先至和卸载，旧回执不会写回下一上下文。共享列表在登录/项目存储先变化而事件未发时也拒绝旧回执，身份只进入内部快照、不进入请求body。

本轮公共前端237/237、原页面与归一规则24/24通过，均0跳过；包含实际Vue选项生命周期及旧列表身份迟到反例。平台真实元数据CLI已在隔离目录成功生成175个归属文件、23个首次手写文件。先前平台两条页面字段仍为ButtonVisibleRuleEditor旧别名，现元数据改为已存在的同一yzp-button-visible-rule-editor组件tag；旧YZP仓库未修改。现有Metarill测试库若已存旧别名，需要通过元数据入口跟进，不自动覆盖用户已修改记录。

平台完整生成仍报告关系/筛选等能力缺口；产物生成成功不表示这些后端已实现，独立前端闭包、HBuilderX与原版浏览器对照仍待后续统一验收。


## 原主子/选择回归与能力门禁

旧简化页面专用的主子、选择、关系和详情探针已逐步换成真实Vue3解析/编译/挂载原完整模板。主子页保留原currentSonItem、ChildList、整entity保存和原parent_id/parent_page_sn路由；换父、卸载、返回事件和nextTick弹窗均隔离旧上下文。type=2保持原工具栏和row[]事件，覆盖稳定顺序、跨页、清空、完整回读/缺项、重试、取消、空父及非法原值。原month枚举、闰月末范围与小数/长整数常量按原类型保留。

本切片原主子/选择/列表/能力门禁定向32/32通过、0跳过，TRX为`backend/tests/Metarill.Generation.Tests/TestResults/original-front-gates-final.trx`对应测试结果；公共前端最近237/237。它们不是MySQL、浏览器、HBuilderX或真机验收，不与其它重叠批次相加。

生成提示、generation-capabilities.json和完整应用导出现在共用同一控件后端缺口审计。原控件布局保留，但未适配能力不能再被描述为“当前只读保护”而继续导出可点击失效的完整应用；FullApplication会明确拒绝，BackendOnly仍需显式选择。缺口必须在后端完成并验收后关闭，不能改原页面做替代控件来绕过。


## 原工程主题与构建核验

项目仓储已读取当前项目自己的name/primary_color，生成器沿原主色展开列表/详情，完整独立宿主只替换原uni.scss中的$uni-primary及tabBar.selectedColor，其余样式规则保持。空主色沿原默认值；非法颜色在生成前拒绝。

冻结e6fe956的独立原页面工程已实际完成npm依赖/闭包检查与HBuilderX 5.26 Web发行，命令包含publish web --webHosting false，未上传托管。产物46文件；461个归属文件前后核验仅私有副本manifest.appid按用户提供标识发生变化，生成页面未被手改。证据见.local/original-demo-ui-evidence-20261020-9b1c54f8/验收记录.md及web-build-receipt.json。该构建不代表后续提交或浏览器/真机交互已通过。

项目主题真实MySQL回读1/1、原完整宿主主题下发1/1通过。随包HTTP探针已迁到原mixins/data/computed/生命周期、saveEntity/deleteEntity/getList；通过真实独立宿主HTTP/MySQL完成登录、菜单、整entity新增修改删除、注销失效及文件引用事务1/1（.local/original-standalone-files-http-v2.trx）。测试明确核对保存body仍含原只读/空字段，不裁剪成新payload。Node只提供表单validate公开宿主，不模拟uni-forms规则或DOM，不当浏览器/设备验收。第一次迁移运行因测试导航栈一直只有一页、错误等待原返回首页分支而失败，已保留v1证据；修正探针真实栈后v2通过，未修改页面返回交互。


## 原数组筛选候选双父补齐

原List筛选继续使用zxz和findNameId数组入口。来源父沿原parent_id；独立目标父只取当前页唯一可读且可筛选的已声明外键，仍由后端验证双方真实父/项目/软删范围。空目标父、非法/溢出ID不枚举候选。目标父改变同步清候选并重新加载，保留筛选原选中值；请求组合固定父快照、来源父和身份/卸载隔离，旧回执不能恢复旧候选。缺映射仍由统一能力缺口约束，不猜关系。

原列表/双父Vue/日期纯值定向27/27、公共前端238/238通过，均0跳过；本批没有DDL或浏览器验收。完整生成冻结bf1cf83曾执行185项，184通过、1项仅在Schema.Apply因测试共享隔离库争唯一锁失败、0跳过，TRX为.local/original-ui-integrated-generation-v1.trx。元数据工作台全69/69、0跳过。结构写用例现统一同一串行collection，不修改产品锁/超时、不加测试重试；新slice整合后仍须完整复验，不能把该185批次写成全绿。


## 平台源码构建与正式再生成

平台前端冻结83dfc79文件快照经HBuilderX 5.26真实执行 `D:/HBuilderX/cli.exe publish web --project E:/git/metarill/frontend/metarill_uni_admin --webTitle Metarill --webHosting false`，exit0，无上传；产物155文件/4,799,789字节，602处本地资源/模块引用无缺失，原logo/六tab/新头像8图与源摘要一致。484个受管前端源前后0变化，用户AppID保持。公共前端248/248、0跳过。证据为.local/platform-web-20261020-83dfc79-6df49e72/验收记录.md及web-build-receipt.json；构建期间后端HEAD推进c82537e，但前端快照完全未变，不据此宣称新增后端逻辑或真实浏览器已验收。

正式CLI从已提交元数据再生成backend/generated/platform和demo，分别175/14个归属输出，均0个新手写文件；生成前后24个手写partial摘要一致，合计189个归属输出已记录首轮摘要。平台元数据摘要b967df4df00b40479a51597b762b424429aaca45790e9c4d6eec36f50162e85b，demo为58ed1e8a8db53a9cbf9fcbd207bc64621a2340083383416e7b047e47e8d97add。实际包含这批产物的API Release构建0警告0错误。平台仍有9项控件/关系后端缺口，保留原UI不代表这些能力已交付；下一切片整合后还需最终重复生成与完整验收。

## 原页面真实浏览器验收与当前边界（2026-10-20）

协调者从冻结c82537e源导出新的465文件独立包，使用用户AppID完成HBuilderX Web发行（47文件、本地引用无缺失）。本轮独占随机表mt_original_ui_db3aef68先由产品SchemaService预览、审查、显式执行和回读0差异；Server-200实际实例与测试库已核对，三张既有文件基础表结构未变。初次私有启动检查误把原静态index标题当作运行后的项目标题而受控停止在seed之前；原始拒绝保留，核对封存HTML字节及已登记进程后续跑，没有修改生成页、重做DDL或重复写入。

实际内嵌浏览器通过原登录、240px左菜单和顶部导航进入原PC列表，12条API预置数据按5/5/2三页呈现。已实际点击原分页、姓名筛选、清空再提交、启用0/1筛选、原日期日历同日范围及金额升降序；原文字“查看”打开完整详情，原uni-forms空姓名必填校验生效。将李宁记录备注及启用状态修改后保存、关闭，列表真实回读金额0、闰日2024-02-29和停用状态均保持。

随后完全通过原新增表单创建一条记录（18.25、启用0、空日期）；先取消复制、再确认复制，列表得到两条不同字符串ID；通过原删除确认软删复制记录，列表只剩原新增行。API预置12条与界面新增/复制两条明确分开。最终独立只读核验14物理行、13可见行、恰1软删，逐条正式GET得到13次成功及1次软删无data，完整字段一致；最终内容摘要为c14b0bf22949fff45f5cdeca3bf873f0e0400d96e925e5e8d69e9f39583d94d6。

原导出按钮在唯一新增姓名筛选下实际发起请求，服务端生成的xlsx已独立保存核验：6803字节、SHA-256为cc5ba95c1dd5be412f5aa83618f5a6811f9def6faa6849a35adac4610029fabb，单sheet、A1:F2、六列表头及唯一新增行一致，金额18.25为数值、启用为原生FALSE、日期空。这里证明按钮请求与服务端真实字节，不宣称浏览器下载文件已在用户下载目录落盘，也不宣称与旧xls格式字节相同。

临时390×844视口下重新启动页面，实际验证原移动卡片、点击加载更多1→2→3/3共13条、原筛选弹层从第三页查到李宁1条及原移动详情；测试后已reset视口并重新加载默认PC页面。截图保留于本任务visualizations目录，分别为metarill-original-list-20261020.jpg、metarill-original-detail-20261020.jpg、metarill-original-mobile-20261020.jpg及metarill-original-mobile-detail-20261020.jpg。响应式浏览器结果不算小程序、Android或iOS真机验收。

退出确认后已回原登录页，但实际发现旧列表onLogout处理仍刷新并额外触发登录失效提示。该失败已保留截图，正在从共享运行时修复并准备新包复验，不手改当前冻结包、不把退出流程写成全绿。原Tiny Cloud配置继续按用户要求留空，复杂关系、主子、文件/导入与各平台完整视觉对照也不能由本单表包替代。私有证据位于.local/original-browser-20261020-db3aef68/control/state；清理草案已核14个已知ID、0未知及0外部依赖，但当前仅只读，尚未执行清理。

主线3811dee的名称适配整合后，platform/demo已正式两轮再生成，189个归属输出及两份归属清单摘要一致，24个手写partial不变，证据为.local/original-re-generation-3811dee。平台能力缺口从9变为8，只关闭已交付名称筛选，未知附加范围仍明确保留。主线用途读取IR36/36、Agent19/19分别通过且均0跳过，不累加为同一完整批次；完整普通Generation组合318/318通过、0失败、0跳过，耗时14分53秒，原始TRX为.local/original-ui-generation-integrated-3811dee.trx；两项FreshInstall专属类显式排除而非计作跳过。包含当前正式产物的API Release构建0警告0错误。ed51af5的远端CI已核对Linux、Windows、前端三项全部成功（PR run39296997392），但该CI不包含后续3811dee/读取IR，也不持私有MySQL配置。


## 退出隔离与原关系引用的整合验证（2026-10-20）

冻结源0da47d9保留原模板DOM、样式及按钮流程，只在共享运行时清理退出后的列表/候选/确认状态，阻止迟到请求；重新登录仍重新读取，不取消全局未认证提示。固定平台组件读取合同及Vue3响应式赋值已整合；角色权限的旧二级菜单/旧角色目标引用改为当前单树菜单和实际角色表，候选继续要求来源/目标页双方授权。上述更改由模板、运行时和元数据源输出，不逐页修补产物。

platform/demo再次正式生成两轮，175/14个归属产物与两份归属清单摘要一致，24个手写partial保持不变；证据为.local/original-regeneration-0da47d9，平台元数据摘要4b6a1159eeaf6532bdbbac1c32090b0dbe0b019417d5b0fe53e6d0662c2006a9，当前仍有6项后端能力缺口。整合实际生成产物的API Release构建0警告、0错误。

分批验证分别为：c78bbcc用途读取IR/固定组件/权限引用64/64、Data真实MySQL41/41；0da47d9前端256/256、原列表/详情/选择合同35/35，全部0失败、0跳过。它们不是同一完整测试批次，不与前述318项相加，也不替代新独立包的浏览器退出复验。旧冻结c825包的退出失败证据继续保留；新包正在准备，未将源码测试称作真实界面成功。


## 原退出流程真实复验与固定查询收口（2026-10-20）

新独立包冻结0da47d9重新导出、Release构建及HBuilderX发行，未手改旧包。root在真实浏览器reload后依次完成登录、原菜单列表13条、原顶栏退出确认、正常返回登录页，再次登录后原菜单重新读取13条；退出不再出现旧包的额外失效提示。该包587项源码/工具封存SHA-256为c7cea81396d4171a39f4cc1e624f90da7a5188455ccc177384619256aa13c4cf；前后4表结构及14行摘要不变，没有DDL、重seed或业务写入。私有回执位于.local/original-browser-20261020-db3aef68-logout-72fe3478/control/state，旧失败包和截图保留。

主线ceb1cc1集中实际固定页面范围和默认排序后，两个config_child名称筛选的原附加项目条件及id desc已证明与实际Query相同，原配置完整保留。主线IR/固定组件/固定Query/权限合同102/102、0失败、0跳过。正式platform/demo两轮191项产物及清单一致，24个手写partial不变；精确剩余4项详情关系Gap，API Release构建0警告0错误。证据为.local/original-fixed-query-integrated-ceb.trx及.local/original-regeneration-ceb1cc1，不与历史批次加总。

平台最新HBuilderX命令仍为publish web --project E:/git/metarill/frontend/metarill_uni_admin --webTitle Metarill --webHosting false，实际成功；155文件、4799996字节，485项受管前端源前后不变，原logo、六页签PNG与新头像均在产物中逐字节核对。回执为.local/platform-web-ceb1cc1-f6518c72。本批仅声明Web构建与上述真实浏览器退出复验，原导入文件选择、复杂关系、真机和同条件性能仍待完成；Tiny Cloud按用户决定继续空key。


## 固定父归属与物理派生值整合（2026-10-20）

冻结8ff90cf已整合两处配置字段List的固定单跳父归属，以及原平台3项物理派生缓存写入。原List继续传既有parentId，服务器核实真实索引/页面及所属表后限定候选；原DOM、props、事件和索引普通CRUD关闭状态保持。派生字段从严格DTO/Agent写入白名单排除，原整entity的已知回显继续兼容；写前从真实目标当前读补实体与submitted，使保存回执正确，改名、排序、复制和自动补字段沿原项目锁及事务传播。没有新增DDL、自动历史回填或实时任意JOIN。

主线离线组合130/130及配置派生规则6/6均0失败0跳过；正式platform/demo两轮191项产物/清单一致，24个手写partial不变，API Release构建0警告0错误。证据为.local/original-bound-derived-integrated-8ff.trx、.local/original-derived-metadata-integrated-8ff.trx及.local/original-regeneration-8ff90cf。平台元数据摘要d4122d5b665b9d9a50d5c237ff561f029d71d403182506bb5416f27797ad9088，剩余角色MultiCheckBoxDatabase与旧Java版本List两项Gap；完整包仍不跳过缺口门禁。

本切片的真实MySQL HTTP、两连接锁及后续观察器回滚另行独占验证，当前不能据离线SQL/观察器测试标为已通过。派生当前读与引用传播增加SQL成本，尚无同条件性能结论。详细合同见fixed-parent-column-relations.md与materialized-derived-writes.md。

## 原Excel选择流程的实际补验（2026-10-20）

root在0da47d9冻结完整包通过原导入按钮及真实filechooser选两行Excel，合法批次显示“导入成功2条数据”。原列表从13变15，原姓名筛选“浏览器导入”精确2条，金额0和-88.5、否/是、2024-02-29及空日期均正确。坏批次首行合法、第二行金额“不是数字”，实际返回失败；只读回查两个人名均0行，原14行全部字段不变。当前16物理/15可见/1软删，新增两行均由root确认归属，最终内容SHA-256为3d5e0c60c52e92784efc31b875485f0a30b6a37c9e62ec7443ca8ceaf682c928。

失败界面目前只给通用服务端错误。实际日志只记录HTTP失败而无异常类型/栈，不能把源码推断的转换异常冒作已采集日志；安全行/列提示与意外异常日志正在独立修复。一次会话过期后的导出初始化实际401，未取得两条导出字节，不能记作下载成功；重新登录后的原按钮另起实际批次。浏览器用户目录落盘仍待单独证据。


## 平台写入清单与远端CI跟进（2026-10-20）

07cb7ef的Linux、Windows及前端CI均已实际通过。后续4c4a431的前端CI通过，两个后端job在“重新生成并核对提交产物”发现平台前端page-write-schema.generated.json未同步；backend/generated本身没有漂移。这是本次整合遗漏，失败批次保留。现通过既有npm run sync:write-schema --prefix frontend/metarill_uni_admin生成21页清单，重复执行changed:false、摘要ab938de7df060415db82a59445b21e76276835b725e7175b51c9fd3a013199f6；仅移除3个派生值的可写声明，页面DOM/样式/事件不改。

同步后完整前端256/256通过、0失败/0跳过；再次实际HBuilderX Web发行成功，155文件、4799766字节，485个受管源前后不变，回执.local/platform-web-schema-6756f59f。修后新提交的远端CI仍应单独核验，不将旧失败批次重标成功。

新会话重新登录后，root原姓名筛选“浏览器导入”2条并点击原导出。真实日志已核initExport200和下载GET200；仅复制该次服务端既有xlsx，无重新请求API或修改文件，6902字节、SHA-256为2717b7400e43438fd4be65883aac82ca03f63729676d5f803d5fb4ef8d67933a。工作表A1:F3的6个原表头、两个人名/项目/备注、数值0/-88.5、原生FALSE/TRUE、2024-02-29/空日期与数据库一致，无公式。证据为新包control/state/export-after-import-fresh-analysis.json及final-import-readonly-receipt-v1.json；浏览器用户目录落盘仍未验证，未借用旧一行文件冒充此两行批次。

用户已确认“yzp版本”原下拉改接Metarill C#模板版本目录，首期一个版本；控件位置及交互保留。该项仍在接线设计，不把确认本身当作已实现。


## 2026-10-20 固定C#版本目录整合与正式再生成

用户确认后，原项目版本下拉已接固定 `Metarill C# v1` 目录，保留候选旧POST、字符串ID、zxz控件和原事件。既有项目null不回填，显式Java/未知版本拒绝生成；名称由可信目录计算，只剥离已证明的只读回显。原平台详情页遗留Java默认ID通过元数据同步入口替换，仅一处初始化值变化，页面DOM/样式/事件其余字节保留。虚拟来源或页覆盖声明不能绕过Full门禁。

正式platform/demo两轮生成191项受管文件及清单一致，24个手写partial不变，包含产物的API Release构建0警告0错误；平台元数据摘要 `b1aedec8b763f22871ed6b501b8b96896756e6aae32f08a4a2d646932d885a43`。证据 `.local/original-regeneration-catalog-20261020`。最新能力清单仅余角色 `MultiCheckBoxDatabase` 一项；完整平台包继续由缺口门禁阻止，BackendOnly不能冒充完整交付。

当前主线目录/导入/父关系相关离线整合61/61、前端完整259/259均零跳过。API真实MySQL整合首批71项中69通过，派生字段真实HTTP、尾部失败回滚、双连接锁序与旧复制别名四项全部通过；两项失败证据保留于 `.local/original-catalog-derived-api-final.trx`。其中CRUD旧测试仍要求整entity删除失败，需与已恢复的原协议修正；平台仅后端导出报“原详情自定义组件名称无效”，正核对实库元数据，尚未称API全通过。安全导入的新发行副本另外封存，真实浏览器精确行列提示待复验，不能借旧generic提示的验收结果代替。

Windows CI上次两项独立包进程超时已通过仅测试层的重量类隔离及启动总预算修正，本机Windows定向60/60零跳过；原构建60秒期限和全部业务HTTP断言保持。GitHub新批次仍须实际核验，未将旧Windows失败改写为通过。


## 2026-10-20 原界面安全导入真实复验

从冻结 `9121dd2` 正式生成的新完整业务包 `original-browser-20261020-db3aef68-9121-safe-import-81397f4c` 已以Release警告视为错误构建，并通过HBuilderX Web发行：47文件、3,831,125字节，119静态引用无缺失；386个前端工厂文件与原 `0da47d9` 业务包同字节，1个手写Service partial不变。只在私有副本填用户AppID和临时开发配置，未对外托管。600个封存文件及6个外部工具摘要通过，seal `0c8ee1131bbd67caa30c1dfc3c4c49ce376d4647be587589e9a4d7e86b4472fc`。

root亲自通过原登录页、原菜单及原列表的文件选择器上传既有“原界面导入-坏批次.xlsx”。实际原弹窗明确显示“第2行，列「金额」：应填写有效数字。”，真实POST返回400，列表保持15条。未重放导入API，未重复导入合法两行；这项真实浏览器结果独立于此前仅generic提示的旧包。

随后独立只读核验：切换前、切换后和坏批次后四表结构摘要均为 `44ff93b809e6a4fdc99706aec6dc9e1ca145860597f8421bb1e19945b217be45`，16行全部字段（含审计字段）摘要均为 `3d5e0c60c52e92784efc31b875485f0a30b6a37c9e62ec7443ca8ceaf682c928`；16物理/15可见/1软删、16已知/0未知、两条坏批次名称各0。确认本次坏批次整批回滚。新包私有 `control/state/safe-import-final-readonly-receipt.json` SHA `734ae3ab79ec1c1206d4d8939ee52027448849859a0950150d73f40cdb89a640`；浏览器观察来源为root，辅助Agent仅负责只读数据库回执，未把离线探针当UI验收。

本机回环服务继续保留以便后续验收，精确状态/日志/停止入口位于新包 `control/运行与停止-v2.md`；原14行切换脚本已过时不得运行。不清理数据、未部署服务器生产环境，也不将开发认证或移动视口称为正式认证与真机验收。


## 2026-10-20 原接口与真实身份后台整合复验

两条旧 `ButtonVisibleRuleEditor` 页级覆盖已通过原config_page_column更新接口改为固定 `yzp-button-visible-rule-editor`，真实管理员权限、父范围、Guard和观察者完整执行。仅这两字段值及正常审计时间变化，五个旁观页字段/四个来源Custom按全字段比对；平台BackendOnly从实际MetadataRepository装载后成功导出，未放宽组件名称校验。

原CRUD测试改为明确验证：未知扩展拒绝、该页未发布的系统字段仍拒绝、已发布整entity回显可正常删除，省略与null规则不变。首批失败日志及一次系统回显范围判断错误的诊断保留；最终冻结产品含真实身份后台策略，完整API **71/71**、完整Agent **54/54**，均真实配置Server-200、零失败零跳过。回执 `.local/original-realm-legacy-api-rerun.trx` 与 `.local/principal-realm-agent-mysql-integrated.trx`。这些结果不包含后续角色工作台/锁协调器的未合并切片。

版本目录另一次窄发布被“数据类型不能为空”拒绝：已有逻辑int64但兼容物理ColumnType为空，第一条请求整笔未变，第二条默认值/提示未发送；34行目标和旁观全字段回查一致，项目版本值未动。此问题单独修复，不能以API全套通过宣称live版本元数据或原平台选择器已完成闭环。


## 2026-10-20 Windows CI启动探针跟进

`2d917ce` 的远端Linux与前端均通过，Windows Generation为454通过/1失败/11专属验收未配置跳过；唯一失败是 `StandaloneShellAuthenticationTests` 在就绪探测中把HttpClient单次5秒超时当成启动失败。之前两个重量类已通过，本次不能把它们的本机60项结果称为Windows全套通过。

只在测试层，将真实构建/启动宿主的认证、目录和导入类加入现有专用collection；认证、导入、目录及导出health探针复用现有30秒启动总预算和自身进程输出诊断。未修改产品、全局并发、子构建期限或就绪后5秒业务请求，不重试密码、会话、导入、导出和权限业务。实际本机Windows定向58/58零失败零跳过，回执 `.local/standalone-health-budget-integrated.trx`。远端修后批次仍需另外核验。

## 2026-10-20 原平台版本下拉与工作台身份范围复验

root在冻结 `ccfb935` 的原平台Web发行副本使用原登录、选择设备/项目、项目列表和新增表单，展开原zxz版本下拉：默认值及唯一候选均为 `Metarill C# v1`，提示已同步；未保存项目，关闭后原列表仍为2条。页面标签、位置、样式和事件保持，未用新页面替换。证据 `.local/platform-version-preview-ccfb-41b2bc10/control/state/platform-csharp-version-cua-after-receipt.json`，封存717文件、3个外部工具核对通过。辅助Agent仅负责只读SQL与脱敏日志；没有事前项目SQL投影，不将CUA的未保存观察写成数据库before/after证明。该副本的静态下拉成功与live元数据发布是两项验收，后者当时仍被旧空物理类型阻断。

用户已明确选择沿用原V8页面编辑权限及真实目标后台归属，不增加超级身份门槛，见 `role-assignment-policy-decision.md`。工作台A切片按真实身份后台读取目录、授权与revision，不再以开发工作区项目作为权限归属；同项目其他后台资源过滤。整合后的完整Agent真实MySQL **63/63**、零跳过，证据 `.local/principal-workbench-realm-integrated.trx`。该切片未实现后续稳定锁序与锁后当前读，不以此关闭撤权、并发或角色控件缺口。
## 2026-10-20 窄范围守卫与旧逻辑类型整合

已将准备阶段目标ID和固定安全归属提示、Mapper写前最终守卫作为兼容合同加入生成器及Core；默认实现不改变旧宿主权限，敏感/非ID范围继续原查询且不进入新上下文。整批导入外层提示早于首父锁，每行仍复用原事务入口；这不是完整预锁协调器。两个独立切片在生成器同一位置合并时，保留锁后原范围与原类型两份快照，均在请求合并及手写钩子之前。正式两轮191项文件/清单一致、24个手写partial不变、API Release 0警告0错误，回执 `.local/original-regeneration-guard-types-71b1fd2/receipt.json`。

整合离线生成守卫/范围/类型26/26、类型及真实观察器44/44，均零跳过，证据 `.local/guard-types-generation-integrated.trx` 和 `.local/guard-types-metadata-integrated.trx`。旧物理int的ID/sort推导保持原规则；显式逻辑类型冲突、未知物理类型和原必填规则继续拒绝，不改原表单。

完整API真实MySQL本批 **70/71**：唯一失败为平台管理员选择业务项目后，角色工作台snapshot被新身份后台限制拒绝，见 `.local/guard-types-api-mysql-integrated.trx`。正在对照原V8区分操作者身份与目标角色后台；不能把先前63项Agent通过或其它API通过冒作该路径兼容，也不将测试改成期待新拒绝。live版本元数据窄发布与角色控件缺口仍单独推进，未据本合同关闭。
## 2026-10-20 按原V8恢复角色工作台入口

原Java工作台的真实调用链明确要求同登录后台，并允许本后台super或角色配置页相应动作权；它没有平台超级管理员这一额外门槛。旧C#API夹具用平台身份跨后台管理业务角色，与原规则不符。已保留原失败TRX，改为同业务后台真实super完成授予查询权、普通账号只读且不能新增、菜单单独不授读、撤权后拒绝的完整链，同时明确平台super选择业务workspace仍不能管理其角色。

工作台分别核对真实操作人链和真实role→admin→project，保同后台限制；query-only可以读取但不能保存，update-only保存回执不额外要求query，避免提交后因回执再鉴权失败。仅已发布真实角色页的开放动作参与普通账号授权。独立贡献53项离线、1项API及4项Agent真实MySQL全部通过，主线整合后完整Agent **72/72**、零失败零跳过，API Release 0警告0错误，回执 `.local/workbench-original-agent-mysql-integrated.trx` 与 `.local/workbench-original-integrated-release.log`。整合后的完整API仍待后续元数据现场收口；没有把5项窄验证称作全71项通过。

本片没有完成锁后当前读或完整普通角色CRUD门禁恢复。迁移暂态角色CRUD拒绝测试已明确标注为暂态，不能将其当作用户确认后的最终策略；角色复选能力缺口仍保留。

## 2026-10-20 live版本窄发布的写后停止与只读诊断

在干净冻结d6ddf22、固定Server-200实例和34行逐值旧快照核验后，第一笔原config_column更新业务200，版本声明已成为固定C#目录，旧空物理类型与int64逻辑值均保持。写后核验发现图从34行增加为37行：C#观察器在普通字段更新中误调用全表Ensure，补出db_password、id、delete_no三个页字段，因此在GET前停止。第二笔页默认值/hint未发送，导出未执行，项目版本值未变；仅本次新会话已注销。

原Java的ConfigColumnServiceImpl只在afterAdd为该新列建立页字段。当前C#普通更新补齐历史缺口不符合原联动边界，正在修源头；新增三行仅保留现场，尚未撤销，不能将其静默纳入预期或声称整个窄发布成功。私有证据为贡献clone的 `.local/version-publish-v2` 和 `.local/version-after-diagnostic`，不提交含配置原文的快照。
## 2026-10-20 原角色复选合同正式再生成

原账号页继续使用原 MultiCheckBoxDatabase 复选、全选、候选 POST 及表单事件；列表和只读详情按原名称数组显示。服务端生成固定候选、精确字符串 ID、数组/旧数组文本与省略/null 合同，以及有界批量名称补充；未新增页面、DOM结构、样式或后台切换事件。固定账号服务的运行时事务协调尚未验收，无论隐藏、改坏或移除角色字段，完整平台包仍由独立 runtime 缺口阻止发布。启用导出或 type=2 选择回执时另列各自未接入的用途缺口，不能借读取合同通过而提前放行。

最终源头 `829ecf0` 正式 platform/demo 两轮生成191项受管文件及清单稳定，24个手写partial摘要保持，包含生成产物的API Release构建0警告0错误；证据 `.local/original-regeneration-final-role-gates-20261020`。主线角色/固定控件/读取关联回归130/130、前端完整260/260，均零跳过。元数据完整离线105通过/22项因无实库配置明确跳过，不把它当数据库验收。证据分别为 `.local/final-role-gates-generation-integrated.trx`、`.local/final-role-gates-frontend.log`、`.local/final-role-gates-metadata-offline-integrated.trx`。

版本发布现场仍保留此前34→37的停止证据；本段只记录源头修复、离线集成及正式生成，不表示三条误增映射已撤销、第二笔默认值/提示已续发，后续单独记录真实执行结果。

## 2026-10-20 版本配置修复与只读补充闭环

用户直接授权四笔精确修复后，在干净冻结 `813f051` 的 Server-200 `metarill_test` 通过原生命周期执行三条误增映射的逻辑删除及第二笔默认值/提示更新，发送4、HTTP成功4、逐笔完整图确认4。三条物理记录保留为墓碑，原墓碑不变；最终37物理/4墓碑/33活动，恢复原活动集合。已成功的第一笔来源配置未重写，项目版本未回填，未执行DDL、seed或硬删除。原GET、实际MetadataRepository、三个候选POST均通过；字符串版本ID精确保持 `2300000000000000001`。本次新会话注销200，并由后续实际请求证明失效。

正式BackendOnly ZIP共268条目，SHA256 `72ca4b73f68f0f892724e21a71832114cba440d2488ec208656ac8db1450c1d4`，无旧Java版本表查询，保留角色缺口提示。原v3私有验收工具因混合路径分隔符误判解包越界而停止，原失败stage、attempt和4笔写入回执完整保留，未重跑。另一个独立只读补充工具规范化目标路径后核对已存ZIP、安全解包并完成独立Release构建（0警告0错误）；最终37行及3个来源列逐值等于四写后快照。补充没有HTTP、登录、数据库写入或宿主启动。最终回执位于贡献clone `.local/version-verification-supplement-v1/evidence-20261020032140-7e407d3117c147a88aa2f3dbaafc4e27/result.json`，SHA256 `796561b76f2624ca4cc9c289a18ee0adc985d7e834467f7a057a297b571da5ba`。本段不将BackendOnly包构建当作完整平台前端/角色验收。

## 2026-10-20 原创建语义与锁后当前读整合

普通新建页及页面换表只校验，不自动补齐历史字段；复制页只复制源页活动字段、按钮和出向子关系，不复活遗漏/软删字段。显式从表建页与新表默认页使用同一内部初始化入口。新增列只给各活动页建立本列映射：页数不超过2时默认显示/编辑，否则仍建映射但隐藏；sort_no默认99且必填、联接列只读、排序缓存保留来源null。8个显式系统列、当前项目范围和直接后台归属仍是C#模型边界，不声称本片完全等同Java所有建表行为。见 `original-page-creation-defaults.md`。

身份聚合与锁后当前读共用同一纯决策；安全写协调器已具备项目/admin预锁、同事务当前身份复核、最终合并角色及归属检查的代码和离线合同。当前尚未注册到宿主、未改变手写角色partial或关闭runtime缺口；最终DI、工作台共用协议及真实RR并发另行验收。

主线完整回归使用核实后的Server-200隔离配置：API71/71、Agent125/125、Metadata150/150、Compatibility29/29，共375项零失败零跳过。各工程也包含纯离线合同，不能把总数冒充375个真实SQL锁案例。证据为 `.local/original-creation-fixtures-{api,metadata,compatibility}-mysql-final.trx` 和 `.local/original-creation-current-realm-agent-mysql-integrated.trx`。此前API70/71和Metadata149/150失败回执保留：分别为旧夹具依赖换表自动补字段、归属种子遗漏真实表名派生缓存；只修正夹具，保留产品校验、原HTTP拒绝和完整图不变断言。

远端 `d6ddf22` 的 pull_request run `39358436796` 三作业通过；同SHA push run `39358433507` 的Linux通过、Windows和frontend失败，不称六项全绿。失败分别是启动预算单元测试的毫秒期限、CLI过期令牌测试在令牌响应前触发请求超时；具体修复及新CI结果另记。

## 2026-10-20 安全写协调正式接线与完整回归

平台实际宿主、三个安全对象的手写partial及角色聚合工作台已使用同一写协调器。实际注册顺序为安全协调器先于配置守卫；普通角色管理遵循用户确认的原V8页面动作权与目标后台规则，不额外要求平台超级管理员。锁后身份、目标最终角色归属与已准备事务上下文均校验，原角色复选页面结构、样式和交互未改。实现与未验边界见 `security-realm-write-coordinator.md`。

冻结源码 `9909c215866b09267076d33e652aefe048ce1ebb` 完整执行API71、Agent142、Metadata150、Compatibility29，合计392项通过、零失败零跳过；各工程同时包含离线合同，不将392冒称392个真实SQL锁案例。回执为 `.local/realm-wiring-{api,agent,metadata,compatibility}-mysql-integrated.trx`。本地助手完整29/29与真实独立宿主1/1通过；此前CI两处毫秒期限竞争仅在测试层修正，未放宽产品期限或增加业务重试，新远端CI须另核。

平台及示例两轮正式生成191项受管文件和清单稳定，API Release为0警告0错误。24个手写partial以新基线核对：其中Manager、Role、Admin三个文件是此次有意接线，另外21个与旧基线保持，重复生成不覆盖任何手写文件；回执 `.local/original-regeneration-coordinator-9909c21/receipt.json`。

平台元数据构建已改为使用仓库内结构参考，不再隐式依赖D盘Java源码。完整重建、负变体与两轮稳定性6项通过，现有23表/649列/20页/662页字段及已发布标识不回退。详细边界见 `platform-metadata-source-protection.md`。

真实RR等锁撤权、锁集变化回滚、跨后台引用并发及最新原页面写入尚未完成；现有索引现场只读比较为三条非唯一ADD INDEX候选，尚未执行DDL。独立包只下发Core/Data/Files/Exports，没有平台三个Runtime扩展；BackendOnly能构建不等于角色完整下发。平台Full另有五个页面的原Custom组件独立宿主接线门槛，实际先被该门槛拒绝，角色Runtime Gap也继续保留，二者不能合并成一个已解决问题。

交付遵循2026-10-20用户更新规范：开发分支仅用于过程审查，最终完成并验证后合入main并推送；当前验收未完，PR保持草稿，不据本检查点宣布全部完成。
## 2026-10-20 最新原角色页面与后台名称投影

冻结产品9909c21/记录d2c1398，复制完整487个原前端源文件，仅私有API端口8088→8288；HBuilderX 5.26实际发行155文件、4799755字节。真实浏览器使用原登录/项目选择/角色新增和zxz后台下拉，创建本轮普通角色，保存排序99，原查看/修改/回列表核对100，再由原删除及确认逻辑删除；独立只读SQL证明自有主键物理1、活动0且排序100。账号新增页原角色复选逐项、全选、取消均有截图，未填写密码或保存账号；管理员列表名称数组显示正确。原退出回登录页后，只停止本轮18820/33444，旧四个封存服务保持。

证据 `.local/original-role-ui-d2c1398-20261020/control/browser-receipt-v2.json` SHA `14d777aa74081b29d958147cc4b852763fb136fa1261c3949afc144c36e96822`。没有事前完整SQL投影，不把界面未保存观察说成全库before/after证明；旧空回执不计验收。

浏览器发现角色列表后台名称空白：原页消费yzAdminTypeName，而C#只有后台ID。源头已补固定角色→真实项目后台的计算名称，不恢复冗余物理列，不新增目标页query门槛；列表、详情、创建/更新/复制回执统一有界批量读取，保原模板/样式/事件。集成者25项后台名及旧能力合同、5项失败诊断均零跳过通过。两轮191项受管文件/清单稳定、24手写partial保持，API Release 0警告0错误；回执 `.local/original-role-admin-regeneration-69b23c9/receipt.json`。新名称尚未浏览器复验，原完整账号表单密码桥接另片进行，不称账号新建/登录闭环已完成。