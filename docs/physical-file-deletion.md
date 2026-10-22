# 带文件字段的物理删除

状态：B32固定MySQL后端、生成与完整包验收已完成（2026-10-23）。冻结源码 `43fd5e9dfd13`，1155个源码／测试／元数据／模板／前端文件。文件全量101/101、平台跨库文件组合7/7、扩大生成组合152/152及原API9/9分别通过，均零失败零跳过，不相加为单次全量。

## 问题与实现

B31实际HTTP发现，物理DELETE后文件观察器要求读取原记录，导致整笔操作被拒绝。现在生成Service先按原身份／页面范围锁来源，完成BeforeDelete和写前关系／守卫校验，再调用兼容默认的 `BeforePhysicalDeleteAsync` 准备阶段；随后执行真实DELETE，传播关系缓存，最后通知原提交观察者。

准备阶段由文件观察器重新当前锁读真实来源、固定页面条件和真实父记录，不接受上下文提交字段冒充原行。只承接具有真实项目列、没有delete_no的普通物理表；逻辑删除沿原路径。证明保存在观察器内部，绑定真实事务、页面、表、记录、项目和身份，不能由HTTP设置；重复准备不能覆盖，跨事务、换观察器或身份、缺证明均拒绝。

删除后按主键核验任何项目下的记录都确实不存在，再一次性消费原锁定快照、重新核父范围，解绑本表文件引用。引用墓碑、业务删除、名称传播及尾部观察者同属原事务；异常整体回滚。物理对象文件不在业务删除中直接移除，提交后再按原文件库删除规则判断是否仍有活动引用。

平台跨库包装器按已发布业务槽复用内部观察器，证明与引用都在生成Service实际会话内；平台事务不能冒用业务事务。生成Service构造时固定观察者集合，保证准备和提交使用同一实例。新方法默认空实现兼容其它观察者，未新增HTTP接口、DTO或前端操作。

## 当前验证与交付

已验真实文件上传／绑定、删除与引用共同回滚、成功后才能删除对象、一次性证明、缺准备／换实例／换事务／换身份／页面拒权／源仍存在／项目越界／固定条件及无效真实父范围；独立NOWAIT确认来源／父行锁，以及另一个文件删除取得文件行锁后等待解绑事务，后者回滚后仍拒删对象。

| 验证 | 最终结果 | 范围 |
| --- | --- | --- |
| 文件模块全量 | 101/101，零失败零跳过 | 含3个新增物理删除实库场景及原文件／父范围／权限／事务回归。 |
| 平台跨库文件组合 | 7/7，零失败零跳过 | 固定发布槽／项目拒绝与正式业务会话，新增物理删行／引用解绑回滚及对象删除；不是整个元数据工程全量。 |
| 扩大生成／实际HTTP／完整包 | 152/152，零失败零跳过 | 两种删除模型均带文件，尾部异常回滚记录／引用／多级名称缓存；同版Core／Files源码包restore/build、Full更新和手写保护；相邻多值／非冗余／维护及110项内存Web认证。 |
| 原API与相邻合同 | 9/9，零失败零跳过 | 原维护接口、菜单及兼容路由；不替代原浏览器。 |
| 正式生成 | 两轮215文件逐字稳定 | 189归属输出、2清单及24手写；24个Service只改观察者集合快照及相应摘要，无手改产物。CLI／API Release零警告零错误。 |
| 前端／模板／元数据 | 与80700cd字节保持 | 无页面、样式、布局、组件、交互或接口字段改动。 |
| 生成HTTP样本后验 | 两轮随机五表及自有文件元数据／引用均不存在 | 独立连接核对数据库／主机／实例UUID，只读后验，未补DROP／DELETE；文件和跨库模块夹具按各自登记ID清理。 |

实际命令为文件工程 `dotnet test backend/tests/Metarill.Files.Tests/Metarill.Files.Tests.csproj -c Release --no-restore`；元数据工程按 `ProjectFileRuntime`、API按 `SystemMaintenanceMySqlTests|CompatibilityAliasRoutingTests|MenuContractTests`，生成工程按 `MixedRelationPropagation|RelationMaintenanceGenerationTests|RelationPropagationGenerationTests|GeneratedMultiSelectMySqlTests|SelfNamePropagationMySqlTests|DatabaseCheckboxMySqlTests|NonRedundantGeneratedMySqlTests|StandaloneWebAuthenticationTests|MultiPageNameCacheTests` 过滤；实库须提供私有配置、平台元数据和实例pin。

实库只使用pin过的Server-200隔离库和随机表；未用本机Docker或新增全局观测权限。共享Web认证测试使用内存数据库，固定HTTP身份不等于正式MySQL登录，构建不替代原浏览器／真机。

已有表结构无需DDL或数据回填。必须同步同版Core／Files／Runtime和生成器，再通过原在线助手正规更新受影响Service及完整包Foundation；仅替换运行时而保留旧物理删除Service不会获取证明，仍会拒绝，不能跳过校验兼容。逻辑删除的元数据和前端保持原样，手写partial继续保护。

B31原失败与原基线保持在原专项目录；本轮私有TRX、日志及回执位于 `.local/physical-file-delete/`，未提交数据或环境配置。首次测试漏带实库变量导致3项跳过且有两处测试不可达代码警告，已修正并保留原日志，不算实库验收。

B32仅为本文件固定MySQL后端／生成／包范围；整个复杂关系、原浏览器、跨端、空库组合、同条件性能及MySQL首版总验收仍未完成。不合main、不部署生产。
