# 原平台 List 搜索与滚动分页接线

日期：2026-10-30。C55 起点：`28a57a2f771e0a1190271f938a6ad4ae5eff27e0`；本任务代码与进度表同提交。用户已确认保留原控件，接入搜索与滚动分页。

本轮完成保留在主前端的十二个原配置页面、十六个标准 List 字段接线。C54 已完成生成模板、详情/筛选与独立包；这里补齐主前端原页面，沿用原 zxz、布局、样式、change 回调、查看/保存/关闭等操作。未重写页面，未改数据库、权限策略或元数据。

## 接线范围

以下页面位于 `frontend/metarill_uni_admin/pages/yzp`。只为表中字段增加生成描述对应的 remote 属性、共享 mixin 和候选初始化；其它请求与脚本保留。

| 原页面 | 接入字段 |
| --- | --- |
| `config_custom_page-detail-2062361618137833473.vue` | yzAdminTypeId |
| `config_index-detail-2076977531071803393.vue` | configTableId |
| `config_index_column-detail-2076977554350190593.vue` | configIndexId、configColumnId |
| `config_page-detail-1868973685063311361.vue` | yzAdminTypeId |
| `config_page-detail-6191175925956608.vue` | configTableId、yzAdminTypeId |
| `config_page_column-detail-6218036784398336.vue` | configColumnId、configPageId |
| `config_table-detail-6191083005411328.vue` | yzAdminTypeId |
| `yz_manager-detail-6031906822946816.vue` | yzAdminTypeId |
| `yz_manager_devices-detail-6220589291798528.vue` | yzManagerId |
| `yz_manager_devices_project-detail-6220608155484160.vue` | yzProjectId、yzManagerId |
| `yz_role-detail-1692532858529329154.vue` | yzAdminTypeId |
| `yz_role_permission-detail-2063264600601714690.vue` | yzRoleId |

原角色权限页的 `configSecondMenuId` 与已确认单菜单树的 `ConfigMenuId` 不是同一个标准字段；本工具不猜测替换，也不把专用编辑器改造成 List。此处未完成的旧菜单配置与完整原界面回归仍按原任务单列。

两个 config_page 原页面仍写旧字段号 `2200000000000000104`，当前编译描述为 `2200000000000000105`。同步只允许这两个固定页面的已核对映射，其它未知编号拒绝。后端 `Controllers/ConfigPageOptionAliases.cs` 用同程序集手写 partial 保留两页旧数组、`/page`、`/selected` 共六个 URI，直接调用当前105生成方法，不复制查询、不绕过来源/目标权限。生成 `.g.cs` 未改，后续再生成不会覆盖此文件；别名仅服务平台旧页面，不加入普通业务包的表级模板。

## 元数据同步与保护

`sync-original-list-options.mjs` 从当前 C# 生成 Vue 的 JSON 字面量读取固定字段描述，用已锁定的 Vue/Babel 解析器核对原标签与请求。只接通实际存在且匹配字段、候选数组及 id/name 合同的原 zxz；不执行生成页脚本，不接受客户端指定表或路由。

- 保留原事件、DOM、其它属性和全部样式，只添加模板已声明的八个 remote 属性/事件。
- 只替换原 `initLists` 中已核对的标准候选请求。额外业务回调、未知参数或参数值、重复属性/请求、未知 this、冲突导入和手工篡改的远程描述均拒绝；所有页面先通过检查再写入。
- 重复运行无变化。`--check` 只读，若仍需同步则非零退出。
- 开发生成命令和 CI 在元数据生成及现有同步后运行同一脚本；不靠逐页补丁维持接线。没有增加 npm 依赖。

在仓库根目录执行：

```powershell
npm run sync:original-list-options --prefix frontend/metarill_uni_admin
npm run sync:original-list-options --prefix frontend/metarill_uni_admin -- --check
```

原平台页没有生成宿主的 `permissionsReady` / `recordReady`。共享 mixin 通过显式 `originalHost:true` 兼容：有既存 ID 时，原详情必须成功回读同一字符串 ID 才启动已选读取；新建页按原身份准备。运行时只更新候选视图，不写 entity 或 filters。测试曾复现 undefined 就绪值被底层视为允许的问题，显式布尔化后修正；未放宽服务端授权。

## 实际验证

| 层次 | 结果与边界 |
| --- | --- |
| 全量前端 | 316 通过、0失败、0跳过。包含同步保护、原页面 Vue 挂载、原宿主就绪/未就绪、已选/失效/只读回归。首次314项中3项旧测试夹具失败，改为实际 Vue 与新分页合同后复验；原失败保留。 |
| 相邻生成与包 | 10 通过、0失败、0跳过，覆盖原 Vue、双父范围及普通/管理完整包。管理包实际 Node 闭包检查、移走模块必失败反例、自身严格构建与 Production 匿名入口另计，不替代正式数据库场景。 |
| 真实 MVC 别名 | 新旧数组/分页/已选 URI、匿名及来源拒绝/目标拒绝检查顺序通过；测试使用不可达回环配置和仅观测的拒绝授权器，不执行 SQL。先复现旧104为404；接线后测试曾误期望内部拒绝文本，按现有脱敏文本修正，产品策略不改。最终专项4项零失败/零跳过；完整API为85通过、0失败、49项条件未满足跳过，跳过不计验收完成。 |
| 原页面保护 | 以 `28a57a2` 的十二页为冻结基线，仅去除精确批准 remote 属性后原 template/事件与 style 摘要一致。另从 `git show` 重放严格同步，十六字段输出逐字等于当前文件。 |
| 再生成 | 平台/示例重新生成并运行四个同步步骤，712个前端与生成文件稳定、0变化；dev.ps1 语法检查0错误。新增后端别名为手写 partial，不在可覆盖目录。 |
| 原工程 Web | HBuilderX 5.34.2026102909、Vue3、实际原平台工程发行成功，沿用配置的 Metarill AppID；没有以 Vite 另建页面。 |

本机实际 Web 命令（工程为本任务独立克隆）：

```powershell
D:\HBuilderX\cli.exe publish web --project E:\git\metarill\.local\paired-baseline-20261028\metarill-source\frontend\metarill_uni_admin --webTitle "Metarill 原平台List验收"
```

产物为上述工程的 `unpackage/dist/build/web`；构建日志保存在私有验证目录，不提交产物或日志。HBuilderX 界面保持用户当前状态，未关闭其它工程。

## 原平台浏览器子集

ego 空间29加载上述实际原工程 Web 产物，以仅监听回环的合成 HTTP 提供5014条内存候选，经过原登录、项目/设备选择、菜单、角色列表、查看进入原详情；不是新增演示页面或固定生成器试验页。

| 原页面/步骤 | 核对结果 |
| --- | --- |
| 角色页原下拉 | 首次展开前独立读回第101页已选；展开取50条，用实际 CDP mouseWheel 输入使原滚动区移动并追加至100条。 |
| 角色搜索/保存 | `%_'` 按字面搜索，选择后用原保存按钮写入本轮内存；刷新重开独立读回字符串 ID `9007199254750012` 的名称。 |
| 自定义页分页失败 | 下一页故意返回500，原错误弹窗出现、已选及已存值保留；原“加载更多”重试成功至100条。 |
| 自定义页搜索/保存 | 搜索后台5009、原选择与保存、刷新回读字符串 ID `9007199254750009`。 |
| 原清空 | 原清空图标仅清空未保存表单，持久化内存仍为5009；原组件冒泡展开行为保持。 |
| 原退出 | 原头像菜单“退出”及确认后回到登录 DOM，合成 token 后续请求401。 |

最终回执：旧 List 数组调用0，未知 API 0，内存更新恰为2次且 ID 为字符串。该浏览器接口不连接 C# 或数据库，不能据此声称真实身份/权限、MySQL持久化或正式注销已验收。CDP 属于浏览器软件输入，未验证实体鼠标硬件、移动触摸或真机。

夹具首次遗漏原列表需要的 `map.statistics`，只修夹具后重新验证，未改原列表。一次等待把输入值当成正文、一次原 modal 后旧元素引用失效，均据实际 DOM/请求重新定位，未重复不明结果的写入。

私有证据目录：

- `E:\git\metarill\.local\original-platform-list-browser-20261030`：browser-receipt.json、原源码重放、regeneration-proof.json、真实 MVC TRX、六张截图。
- `E:\git\metarill\.local\large-relation-browser-20261030`：前端316项日志、10项包/原Vue TRX、原平台 HBuilderX 发行日志及就绪红灯。

截图01/02/04/05已逐张核对稳定界面；03/06处于弹窗动画阶段，只作为过程记录，不作为稳定视觉验收。空间29已结束，本轮 HTTP 服务精确进程已停止，5311/8088确认无监听。没有遗留本轮浏览器服务供继续操作，也没有上传截图或私有验证数据到公开仓库。

## 未完成与回退

V02仍为部分完成：真实 MySQL与普通登录/授权、全部父范围和详情打开方式、其它专用候选、多值控件、小程序/Android/iOS及物理输入仍待。原父参数的字符串 `undefined` 在无父定义页面被服务器忽略，不把这轮无父页面当作有父范围通过；有父定义仍必须按原合同提供有效父记录。

没有消费 C53、B102、C38 的待授权租约，没有创建远端库/账号/表、DDL/DML或部署；既有固定正式包未改。若回退，需配套回退原页同步输出和共享 mixin，再生成后核对，旧数组5000条上限仍存在，不能静默截断或一次后台拉全。仅移除新后端别名会使旧104客户端重新404，应与页面版本一同评估。
