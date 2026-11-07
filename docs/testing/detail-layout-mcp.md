# 原详情布局MCP聚合

日期：2026-10-30；起点 `d70b125`。本项接回原布局MCP功能，复用[C59布局规则和事务](../detail-layout-consistency.md)，不修改前端、元数据、表结构或部署。

## 工具与原合同

平台 `/api/mcp` 新增原工具名，现有工具保持：

| 工具 | 参数 | 结果与边界 |
| --- | --- | --- |
| `getPageDetailLayout` | `pageId`字符串 | 原布局、全部字段归属及revision，包含pageColumnId和columnId；只读标记，不提供身份/项目/SQL参数 |
| `savePageDetailLayout` | `request`完整对象 | pageId、最近revision、detailLayout(version/mode/groups)及全部fields(pageColumnId/detailGroupKey)；调用保存即在原配置事务内执行，不增加前端步骤；不DDL、不生成或部署 |

原成功JSON文本继续为 `{ok:true,message,data}`。原失败形状继续为 `{ok:false,error:{code,message,retryable,outcome,recovery,traceId}}`，MCP的IsError与失败一致。原HTTP继续使用R及其409业务码，不把MCP envelope写入HTTP。

原Java来源为 `PageTools.getPageDetailLayout/savePageDetailLayout`、`ToolJsonResult`、`McpToolErrors`；保留原大小写工具名和外层request参数。新版完整应用的公共MCP仍只处理自己的生成页面，不获得平台设计器工具。

保存使用严格类型请求及JsonRequired/JsonUnmappedMemberHandling。实际tools/list给出完整嵌套必填结构，服务、身份及取消令牌不作为客户端参数。SDK绑定不得先丢弃额外属性，再假装业务白名单已校验。分组64字符、完整归属、合法ID与revision继续由同一聚合验证，revision显式限制64字符，尾随换行在事务前拒绝。

## 逐次授权与失败语义

- 工具和原HTTP都调用实际HttpRuntimeAuthorization与MetadataWorkbenchBusiness；当前身份、当前项目、真实表/页归属、项目/页面/字段锁、revision及事务规则不变。
- 身份缺失/权限前检拒绝使用YZP_UNAUTHENTICATED/YZP_FORBIDDEN与not_applied，不执行SQL。
- 纯布局请求解析使用只能在本程序集参数前检创建的MetadataLayoutInputException。只有打开事务前的解析被包装；这类失败使用原YZP_INVALID_ARGUMENT、not_applied、correct_arguments。
- 实际版本冲突使用YZP_CONFLICT、not_applied、refresh_and_replan。任意执行/提交阶段异常不能因中文提示相似被改判“未写入”。
- 其它执行失败保守返回unknown/read_back_before_retry，直接取消/超时使用YZP_RESULT_UNKNOWN；不自动重试。仅记录追踪标识、分类和异常类型，不回显内部异常、参数或连接原文。

这两项工具仅在真实平台Program显式注册。没有给Agent元数据写入新增旁路，也不解除平台原权限要求。

## 验证与失败记录

1. 最终完整元数据 **446通过、0失败、25实库条件跳过**。包含19项真实SDK Streamable HTTP／原Controller往返，实际源码与实际授权/聚合/事务，认证状态与ADO传输为本地夹具；不是正式登录或MySQL。
2. SDK验证原工具名、嵌套required/additionalProperties、无服务/身份参数；MCP读取与原HTTP完整JSON值相同；保存后HTTP回读同revision/归属；旧revision同时验证MCP冲突和原HTTP业务409。
3. 未知/缺失/非法嵌套参数、null、超长组key、非法ID/版本及尾随换行在SQL前拒绝；匿名/非管理员在SQL前拒绝，当前项目变化不读取原布局；执行阶段异常正文不返回且不提供安全重试假象。
4. 真实平台Program的工具DI表与原匿名401专测 **1通过/0跳过**，包含于最终完整API **87通过、0失败、49条件跳过**，不重复计88。该检查使用不可达端口配置，不初始化或执行SQL。
5. 公共MCP完整 **30通过、0失败、0跳过**，保持普通应用原三工具集合，不扩大设计器权限。
6. C59的前端320与712文件再生成证据沿用；本项无前端/模板/生成物更改，另外逐字检查712文件仍一致，不声称重跑浏览器或跨端构建。

首轮链接源码编译遗漏JSON/ID命名空间和Web扩展导入，已修；未修改原Controller。首轮实际SDK运行12通过/1失败，是把紧凑HTTP JSON与缩进MCP JSON逐字串相比；改为全部结构和值深比较，保留字段/ID/版本检查，不改生产格式迎合测试。随后13项与440元数据通过；补前检分类、严格revision后以最终446批次为准，不叠加批次。

私有证据 `.local/detail-layout-mcp-20261030` 保存编译诊断、原失败TRX、最终元数据/API/公共MCP TRX、Program注册结果及源码/交付回执。本轮TestServer无外部监听，SDK与宿主均已正常释放，无新增常驻服务。

## 更新与未完成

平台更新本版API及Runtime后重编译即可；不需要DDL或重新生成合法业务页，不修改手写业务。原MCP客户端重新发现工具后可用原工具名；不能把这两个平台入口同步进普通业务应用。

尚未执行真实平台MCP登录/持久身份/数据库、排队撤权、真实MySQL布局并发及原工作台浏览器；不把受控身份/ADO或Program注册当成这些验收。B12、C08与MySQL首版仍保持部分完成。未执行新远端DDL/DML、桶/对象操作或待授权C53/B102/C38等租约，未合main或部署。
