# 原详情方法的动态请求与真实路由核对

2026-11-02，C91；起点0dc8e31，期间B119真实结果已独立提交a24001e。仅补验收工具／测试及CI，原前端、生成模板、元数据和产品后端未改。C92发现的旧页面能力开关差异仍待确认，不因路由通过标记修复。

## 实际覆盖

固定AST扫描仍为111份原源码、三端330条固定声明、62组方法／路径、277条动态未证明记录；不把这些未解析记录改写成固定证据。新增original-detail-requests直接读取平台18个原详情Vue，H5／App／微信分别预处理和解析，只装载原data、methods以及原utils.isBlank函数。不会执行模块import、顶层代码、生命周期或请求网络；动态组件、初始化表达式、计算键、展开和缺失定义在执行前拒绝，不靠正则替换请求变量。

实际驱动原getEntity／saveEntity／deleteEntity与三页copyEntity。四种parent_id为空、历史字符串undefined、历史字符串null、超出JS安全整数的真实父ID；记录ID同样超过安全整数。模拟表单校验结果和原确认回调，仅捕获yzp.request参数，不执行成功回调、业务身份、数据库或浏览器。原保存方法把父ID回写实体的规则保留，没有给原实体增加新的约束。

共捕获900笔实际原方法请求、75组唯一动态路由；另117个校验失败／取消删除或复制分支均零请求。捕获逐项核对原文件所声明的表／页和对应新增／更新动作，拒绝串到另一条有效接口；保持声明的会话发送、GET／POST／DELETE、长ID字符串及父参数，不把方法存在等同于用户当前有权点击。

与原62组固定路由合并，真实Program／MVC逐一核对137组。125组实际匹配控制器动作；12组普通写入口由生产MetadataRegistry当前元数据明确关闭，必须404且不带动作探针标记。包括三个聚合表的新增／修改／删除及字段、页面、页面字段的普通新增；不为消除404恢复越过专用工作台的入口。未知地址404、错误方法405、未登记Legacy动作拒绝三项原阴性对照继续执行。

探针在资源过滤器阶段结束，早于模型绑定、控制器实例与业务动作；使用不可达回环数据库配置并关闭导出清理任务。路由匹配不证明请求体合法、真实登录、按钮授权、SQL／事务、原浏览器或首版总验收。

## 原失败与最终结果

- 首次Node夹具误把原父ID回填判为实体意外修改；核对原saveEntity后保留原回填，仅检查长主键不被改写。日志保留，未修改页面。
- 首次真实Program核对1失败：把全部原方法当成必须开放，出现12个404。逐项读取生产元数据及实际Controller后将这12组纳入必须关闭的阴性校验；没有新增后端动作或改权限。由此发现旧角色权限详情开关落后于已确认的聚合写边界，另列C92，不把该差异算通过。
- 最终Node原AST护栏及新原方法护栏9通过／0失败／0跳过；真实Program定向严格Release、-warnaserror、--disable-build-servers的1项完整路由探针通过。两组范围分别记录，不冒称完整API全量或发布CI通过。

实际命令：`node --test tools/acceptance/original-frontend-routes.test.mjs tools/acceptance/original-detail-requests.test.mjs`；固定SDK下`dotnet test backend/tests/Metarill.Api.Tests -c Release --nologo --disable-build-servers --verbosity quiet -warnaserror --filter FullyQualifiedName~OriginalFrontendRouteInventoryTests`。CI在原AST步骤增加新护栏，原矩阵、权限及其它步骤保持。

私有证据在.local/original-dynamic-routes-20261101：inventory.json、detail-first.json、detail-second.json、node-first／final／closed.log、api-first／final／closed／delivery.log、node-delivery.log、detail-delivery.json及TestResults。delivery-result.json核对最终18页／900请求／117零请求分支、125动作匹配／12禁用404、Node9／API1通过；原776份受保护文件逐字核对0变化；没有数据库写入、原页面／模板修改或部署。

## 继续工作

通用封装、运行时关系／级联／文件动态地址、完整请求体／业务权限及真实浏览器仍需逐项验证。旧角色权限详情三个开关、索引与索引字段两个删除开关的精确差异与待确认方案见[页面能力开关决定](../original-page-capability-decision.md)。本轮不改变已关闭接口的策略，不关闭C63／V01／V10／V11整体门槛。
