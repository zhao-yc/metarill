# 原按钮结构化显示规则（2026-10-29）

本批继续 C18，补原 Java `ButtonVisibilityBusiness.matchesRule` 的后端判断。原前端页面、样式、交互及 `/admin/sys/config_button/execute` 正文保持原样；父范围请求协议仍待用户决定，本批没有修改。

## 实现与归属

- Core 的 `ButtonVisibilityRule` 在发布时编译 `visibleRuleJson`，固定条件/分组和所需字段；不是每个请求解析元数据，更不执行脚本或拼接SQL。
- 发布目录冻结表字段物理名称、原JSON名称及C#生成JSON名称的映射，最终按实体的真实Column.Name绑定，避免大写/下划线字段只靠驼峰猜测，Web中央入口与Agent共享 `AgentActionBinding.RequireRecordAsync`。实际值来自已经通过范围证明的正式生成实体，不用客户端 `entity`、`visibleScheme` 或 `visibleRuleJson` 替代。
- 实体类型/Reader/Service仍按原真实MVC、程序集和请求Scope规则验证。仅反射规则需要且确实带生成JSON名称的属性，不序列化整个实体。未声明属性不读取；已声明但缺失实际属性要求成套再生成；尚未装载的 `IsIgnore` 派生属性明确拒绝，不能把默认空值当作实际关系值。
- 不匹配时沿原Web对象规则返回“当前记录不允许执行该按钮”，发生于执行租约和业务回调之前。Agent同样在真实调用校验阶段拒绝，不因按钮仍有授权就忽略显示规则。

## 兼容语义

支持原 AND/OR 嵌套分组，`eq/ne/gt/gte/lt/lte/in/notIn/isBlank/isNotBlank`，显式 `valueList`、数组 `value` 及逗号/换行分隔列表。空定义、JSON null、空组按原后端为真，未知logic沿AND；未知字段按原Map读取为空，不反射任意对象成员或把字符串再次解析为JSON。非法规则/运算符不会降级为允许；有歧义的重复键不发布该业务按钮，普通页面目录保持可用。

数值沿原Java BigDecimal语义精确比较：不经double，不因decimal容量不足退回舍入值，巨大指数只比较数量级和有效数字，不分配巨大整数。非数值字符串按原Java UTF-16顺序比较，不随操作系统区域变化；空白、空集合、布尔、日期/JDBC时间戳及FastJSON复合值使用对照向量验证。

发布规则最多65536字符、16层嵌套、1024节点和16个点分隔符，超过或结构无效明确不发布该业务按钮，不默认为空规则。完整应用必须成套更新Core和Agent；真实Web完整包的受管更新保护 `src/Metarill.Core/Metadata/ButtonVisibilityRule.cs` 与 `src/Metarill.Agent/AgentButtonVisibility.cs`，不能同时裁源码和清单伪装支持。没有表/字段变化，无DDL或回填；手写partial不变。

## 证据与边界

公开 `tools/acceptance/button-visibility/vectors.json` 和 `ButtonVisibilityOracle.java`。调用器反射执行冻结Java样板原方法，不复制规则算法；Java源文件与 `b6d64fb2ad4ac25b004eeaa326092de3cfb7f311` 的v8模板去除CRLF差异后逐字一致，调用类与原打包JAR逐字一致，类/源码/向量SHA256写入私有回执。61组原Java实际对照通过，包含大整数、超decimal精度、极端指数、Java空白、嵌套/未知字段、真正JDBC日期/时间/时间戳及JSON复合值。

测试还通过原MVC与真实生成包消费实际实体，覆盖元数据后改不影响发布快照、当前记录值逐次读取、客户端伪造规则/实体不能覆盖，以及拒绝发生在业务/租约之前。首轮完整包有错误的“拒绝前无读取”断言，修正为必须存在必要真实读取、不得进入租约/业务/写事务，失败记录保留。

这些证据不等于正式Web登录/OAuth/CLI/MySQL整业务验收。判断发生于原执行前读取；业务事务中的状态、摘要和并发规则仍由原Business/Service负责，不能用显示条件代替事务约束。未装载派生字段、非空只读表达式、父范围协议、跨库当前权限/文件事务、完整应用多实例业务及同条件性能继续待办，不把C18整项打勾；B98范围没有扩大。

## 最终门禁

| 门禁 | 结果 |
| --- | --- |
| 原Java实际规则 | 61组向量通过；源码/原JAR及向量摘要已核对 |
| Data整体 | 954通过，21实库条件跳过 |
| Agent整体 | 309通过，25实库条件跳过 |
| API整体 | 69通过，47实库条件跳过 |
| 相关完整包、按钮权限/更新及受管更新 | 51通过，零跳过 |
| CLI严格构建 | 零警告、零错误 |
| 平台/示例两轮正式生成 | 705文件逐字稳定，490原前端与24手写partial不变 |

实现、测试、进度记录同提交交付，冻结起点 `bf901c554486d2845dfcc8fcec7bffe6100426e4`。普通套件的93项实库条件跳过不算通过；本批没有执行新的远端数据库写入、原浏览器/真机、正式身份或性能验收。

私有证据目录：`.local/button-visibility-20261029`。

## 2026-10-29：只读条件及精确数值跟进

[C21只读条件](button-readonly-conditions.md)已接Web/Agent共用原记录的执行前检查。原显示规则新增5组最终BigDecimal scale及Unicode数字反例，原Java66组实际通过；提取共用精确数值实现修复C#首轮4处差异，保留非数值文字回退。最初61组回执仍是历史证据。当前148定向、Data1055/22跳过、Agent320/25、安全131、API69/47和37项实际完整包/受管更新验证通过；真实身份/实库/并发/性能及复合条件页面生成继续，不能据此将C18整体完成。
