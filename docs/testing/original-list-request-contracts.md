# 原标准列表方法与分页请求合同

2026-11-02，C94；起点706cc23。仅新增验收工具、测试及CI，未修改产品后端、原前端、生成模板或元数据。本项补充[C91原详情路由](original-detail-dynamic-routes.md)，不替代真实业务、浏览器或首版总验收。

## 实际覆盖

清点20个原列表Vue，其中16页在页面内声明列表方法、3页使用原createListPageMixin。工具逐页读取真实SFC和原listPageRuntime.js，分别按H5、App、微信条件编译分支及PC／移动两视口执行原data、methods、列表工厂及回调；只捕获yzp.request，不执行import、模块顶层初始化、生命周期、computed或实际网络。工厂必须导入原精确模块，初始化表达式、未知配置、展开、重复键及另一工厂来源先拒绝。原isBlank与详情工具共享，未另写空值或列表状态机。

实际捕获1824笔请求、19组唯一POST分页路由，执行2679个方法场景，其中1026个按原逻辑零请求。覆盖分页、刷新、空筛选提交／重置、详情保存同ID回写、移动追加、空父页阻断以及成功／错误回调；长父ID保留字符串。筛选场景使用原空filters及orderBy，不证明有值筛选、排序、字段权限或候选关系查询正确。成功回调使用合成记录、分页及统计对象，只证明原方法处理这些回包的行为。

第20页是原菜单工作台包装，三个平台各保留一条明确未覆盖记录；没有因其无methods而静默算通过。动态组件、真实导航／渲染和工作台业务仍另验。C91原AST的277条动态未证明记录保持，未将本轮捕获回填为静态事实。

## 实际Program与请求体

使用不可达回环数据库配置的真实WebApplicationFactory<Program>，关闭导出清理Worker；19条实际路由逐条核对真实动作的Body参数仍是PageRequest。探针在资源阶段返回204，早于MVC模型绑定、控制器实例和动作执行。1824份真实捕获正文另用该Program实际MVC JSON配置解码PageRequest，对照页码、页大小、空筛选／排序及父ID；三种错误形状必须JsonException。既有PageRequest忽略未映射字段，额外身份提示不能进入DTO状态，未改变生产解析设置或权限。

这19条路由已在C91的137组总路由内，不能相加为156组。测试不证明MVC实际模型绑定、登录、权限、SQL、事务、真实统计或浏览器业务通过。

## 原失败及最终结果

首次API定向1通过／1失败：Windows子进程按系统代码页解码Node UTF-8输出，中文未覆盖说明损坏导致JSON读取失败。只在测试管道明确UTF-8，原页面未改。第二次1通过／1失败：19路由和1824正文已通过，末段夹具误把PageRequest未知字段当成必须拒绝；核对原JsonDefaults后保留忽略兼容合同，改验额外提示不能改变DTO状态。三个错误形状仍严格拒绝，没有放宽接口。

最终Node原AST／详情／列表共12通过／0失败／0跳过；API原详情路由及新增列表两项严格Release测试共2通过／0失败／0跳过。两组计数范围分别保留，不冒称全量API或CI通过。CI仅在原AST步骤追加列表护栏，不改矩阵、权限和其它步骤。

实际命令：

- `node --test tools/acceptance/original-frontend-routes.test.mjs tools/acceptance/original-detail-requests.test.mjs tools/acceptance/original-list-requests.test.mjs`
- `.tools/dotnet/dotnet.exe test backend/tests/Metarill.Api.Tests -c Release --nologo --disable-build-servers --verbosity quiet -warnaserror --filter FullyQualifiedName~OriginalFrontendRouteInventoryTests`

私有证据：`.local/original-list-requests-20261102`中的first.json、node-first／final.log、api-first／utf8／final.log、对应三份TRX及delivery-result.json。776份原受保护文件逐字核对0变化；B120单库验收冻结目录独立核对，不消费其待批准租约。C92五开关仍待确认，复杂关系、业务身份、原浏览器／真机和性能继续。
