# 固定多值名称传播

状态：B29固定后端／生成链路已完成（2026-10-23）。冻结源码 `9be81b80a034`，1138个源码／测试／元数据／模板／前端文件。普通冗余MultiSelectPage与数据库复选的物理字符串名称采用发布关系计划；不是整个B21、复杂联接或维护回填完成。

## 静态计划与事务

生成器使用同一来源写入证明，编译稳定表／字段ID，Core/Data验证项目归属、主键、真实项目列、物理缓存、精确路径、标量类型及字段依赖。生成项目只保留一份静态计划，执行阶段不再解析请求元数据或接受SQL。平台固定配置缓存继续其已有专用协调器，不重复挂通用传播。

目标新增、显式提交名称、逻辑／物理删除后，在原Service事务内、通知观察者之前，从消费者真实外键反查活动记录。消费者与目标分别限制当前项目；按主键分批当前锁读，一组来源IDs的多个名称缓存共用目标查询，只写派生列，不改ID、业务值、审计字段或调用消费者Service／钩子。传播异常与文件引用／目标变更共同回滚。

多值顺序保持；数值历史ID精确解析，缺失／删除／越界目标的名称位置为null，原ID不修补；坏JSON无法证明引用时不纳入反查。5000项复选不截断，超发布数组预算明确拒绝。自关联自身缓存补回原实体，写回执与数据库一致。

并发反向传播可能与来源事务形成锁环；MySQL死锁／锁超时明确失败、由外层事务回滚并提示重试，不引入全局串行化或自动重试业务钩子。测试只在本任务随机表上用NOWAIT确认锁，不新增PROCESS或全局锁观测权限。

## 原页面与升级

原页面、布局、样式、组件、弹层及/api接口不变。名称传播属于已授权目标变更的内部一致性更新，不借消费者页面动作权限或回填按钮执行；不会扩大公开DTO/VO。原手写partial保持首次创建／再生成保护。

已有正确物理缓存无需DDL。同版更新生成器与Data，通过原在线助手正规再生成所有受影响Service及GeneratedRegistration，完整包同步同版Foundation。不能只替换某个目标Service留下没有同版项目计划的混合产物。缺少自动缓存列仍按B27审查结构差异、显式执行、核验后再生成；旧记录不会在启动自动回填。

## 验证与剩余

| 验证 | 最终结果 | 范围与证据 |
| --- | --- | --- |
| 扩大生成／HTTP组合 | 138/138，零失败零跳过 | 多页缓存、两类控件、冗余／非冗余相邻功能、固定条件、自关联、传播与完整包；不是整个生成测试工程全量。 |
| 数据层全量 | 245/245，零失败零跳过 | 包括28项传播静态计划验证；与组合批次分别记录，不累加成单次结果。 |
| 真实HTTP | 目标改名／软删／物理删、5000项与事务通过 | 同源多名称缓存同步；消费者页面动作被拒绝仍仅内部更新派生列；其他项目、软删来源、坏JSON和ID前缀保持；尾部观察者故障与文件引用整笔回滚。 |
| 自关联 | 显式／自动缓存均通过 | 返回实体与同事务缓存一致；物理删除保留原ID数组并产生null名称位置。 |
| 真实并发 | 两轮锁环均通过 | 独立NOWAIT确认目标锁；本次目标被选为死锁受害者，HTTP明确失败，目标／缓存一致回滚；不授全局观测权限。 |
| Full完整包 | 包内源码构建与更新保护通过 | 多选／复选两种完整应用从Foundation源码restore/build；原执行器逐字相同，手写partial不覆盖。 |
| 正式生成 | 两轮共215文件逐字稳定 | 189归属输出＋2清单＋24手写；CLI／API Release零警告零错误，原前端／模板／元数据与正式产物无diff。 |
| 样本清理 | 独立核验通过 | 8轮复选、2轮非冗余多选、4轮自关联；另10轮原多选三表及其文件记录／引用只读后验均不存在，不补DROP／DELETE。 |

原测试夹具把原DELETE接口误写POST造成的三轮失败和一次不存在的理论属性编译失败均保留；修正夹具后冻结源码完成上述最终回归。私有原始证据在`.local/relation-propagation/`，未将环境配置、原始日志或数据提交开源仓库。完整命令为`dotnet test backend/tests/Metarill.Generation.Tests/Metarill.Generation.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~MultiPageNameCacheTests|FullyQualifiedName~RedundantMultiNameCacheTests|FullyQualifiedName~GeneratedMultiSelectContractTests|FullyQualifiedName~GeneratedMultiSelectMySqlTests|FullyQualifiedName~DatabaseCheckbox|FullyQualifiedName~NonRedundantMultiSelection|FullyQualifiedName~PageConditionGenerationTests|FullyQualifiedName~SelfNamePropagationMySqlTests|FullyQualifiedName~RelationPropagationGenerationTests"`及`dotnet test backend/tests/Metarill.Data.Tests/Metarill.Data.Tests.csproj -c Release --no-restore`；实库运行须按测试约定提供私有测试配置及实例pin。

R0中间节点／混合链、单值复杂派生、全局／非字符串目标、级联其余组合、完整父／候选条件变化语义及原维护入口回填继续B21／B22；控制项profile只为本已证明绑定标记renamePropagationSupported。正式身份／浏览器、专用空库组合、跨端及同条件性能仍待验收，不以固定授权器／Node或构建替代。

## 后续B31固定混合传播

固定R1→R0→R1、双角色、中间外键切换／删除、后续物理副本及普通多值统一事务队列已完成B31；原维护根缓存改变也继续向下游传播。详见[混合传播专项](mixed-relation-name-propagation.md)。本文件B29原验证记录保留，不能将后续固定范围扩张为B21整体或原浏览器验收；其余复杂依赖仍待补齐。

## 后续B60跨表标量名称缓存

固定R1跨表各级物理字符串名称已接入原传播事务；绑定控制列／层角色，默认name与自定义caption共用原prefix_name缓存，缓存批量回读失败整笔回滚，下游副本继续传播。详见[跨表缓存专项](cross-table-name-cache.md)。B29原测试批次保留，不能扩张为R1重复来源、Full或B21整体完成。

## 2026-10-28 B94：固定派生自树与多选缓存

普通R1来源消费普通R0固定派生字符串名称的自树及MultiSelectPage，现已接入统一传播／原维护；两层名称、同目标混用、下游与跨批回滚按固定范围验证。历史数组预算规则保持，原页面／组件／交互不改。数据789、API83、36门禁、32实库模型、6原Full及123个相邻唯一用例覆盖通过；后者原轮4项配置缺失跳过与精确实库补跑分别保留。仅该固定MySQL后端／包范围，R1派生数据库复选、全局派生自树、正式宿主／原浏览器及其余范围仍继续。完整边界、首因、升级及回退见[B94专项](redundant-derived-self-tree.md)，冻结6ff9806ea6c4。

## 2026-10-28 B95：R1固定派生名称缓存

普通R1数据库复选现支持普通R0单层／两层派生字符串名称，原来源动作、父／表单条件、5000预算及R1 ID数组Excel保持；R0名称数组不变。统一传播、关联切换、软删、原维护／重复零及跨批预算回滚已由六实库模型验证，六原Full、数据814／API83、旧实库8与生成52通过；十组53表独立核零。原546资源及215产物含24手写保持。固定MySQL后端／包之外的正式身份、原浏览器、跨端、性能及未证明组合继续，见[B95专项](redundant-derived-checkbox.md)，冻结17e1204cd062。
