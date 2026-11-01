# 非冗余double读取与生成（A23）

2026-11-02，起点2c08e82。普通double已在A17接入；本任务补实际遗漏的非冗余读取，不改变原页面、接口、响应属性或数据库结构。

## 原问题与修正

非冗余生成器的筛选类型白名单漏了double，字段配置合法却在任何输出前拒绝生成。底层固定筛选用double.TryParse，允许非零指数文本下溢成0，可能意外匹配零记录；通用VO投影的精确类型快捷返回也会放过NaN／无穷，文本溢出／下溢亦未在投影边界拒绝。

仅修改三处源头：

- NonRedundantReadCompilation允许已定义的double筛选，仍走现有固定计划、当前页可读边界与原JSON有限数转换。
- MySqlNonRedundantReader的double文本绑定复用ExactNumericValue.TryDouble，与普通字段、固定条件及仓储一致；标量或IN中的非法元素均在第一次连接／COUNT前拒绝。
- NonRedundantReads.Value在精确类型快捷返回之前校验double。合法驱动数保留浮点语义，文本使用同一转换规则；null和超过JS安全整数的int64不经过浮点中转。

只消费元数据声明的物理测量值和派生double值，没有向前端增加范围列、改变权限、写锁或持久化派生值。double的SUM合同尚未完成，原decimal统计门槛保持；本任务包含该拒绝回归，没有将它伪装成统计支持。

## 实际验证与原失败

证据根为.local/nonredundant-double-20261102，全部原日志和TRX保留，批次重叠不累加。

| 批次 | 结果 | 含义 |
| --- | --- | --- |
| data-before | 5通过／7失败／0跳过 | 五种非法投影及标量／IN下溢均复现；下溢错误走向不可达回环连接，不是Server-200验收 |
| generation-before | 夹具编译失败 | 缺少JsonDefaults命名空间，修正测试引用后才取得功能红灯，不将编译失败冒作功能证据 |
| generation-reproduction | 0通过／1失败／0跳过 | 真正生成入口按“非冗余筛选字段类型尚未接线”拒绝 |
| data-after | 61通过／0失败／0跳过 | 新12个数据检查与读取／double结构相邻回归；合法1.25e100、极小非零值、null和长ID保持 |
| generation-delivery | 26通过／1失败／0跳过 | 原double、非冗余生成及四个既有Full模型已跑；旧全局拒绝断言类型不匹配，见下节 |
| generation-final | 11通过／1失败／0跳过 | 新Full检查误把打包器既有LF转换当源码差异，尚未进入该包构建；仅修测试按精确CRLF/CR→LF规则比较，其他内容逐字符相等 |
| generation-final-fixed | 12通过／0失败／0跳过 | 两个新double用例、六个原精确拒绝门禁、四个实际数值运行时更新门禁 |

新生成测试真正编译并执行VO及Service筛选方法，覆盖原列表／详情／type2，物理与派生double、1e100级大数和极小非零值、标量／IN、未知范围不进入VO、真实项目／父键int64及字符串大ID、null／非法值拒绝、两轮手写partial保护。

同一double元数据由正式ApplicationPackager导出FullApplication，包含原页面及当前两个Data读取源文件；用包内项目严格Release构建、警告视为错误，不引用原仓库项目或使用BackendOnly替代。C#源码沿原打包器明确LF规范核对；原前端与手写资产仍逐字保护。子构建输出另保留于.local/nonredundant-double-package-builds的本轮GUID目录。

首轮过滤器使用了LocalGenerationUpdateDoubleTests文件名，但实际类型为LocalGenerationUpdateTests，未覆盖那四项，不能据此宣称更新器已验。最终改用实际方法Candidate_requires_complete_numeric_runtime_without_touching_target，TRX逐类型核对2／6／4，四项确实执行。validation-receipt.json固定测试名、原始计数和六份源码／测试摘要。

## 既有非法全局父变量断言（C93）

相邻失败只发生在Full_application_preserves_specific_capability_gates(global)：带父上下文变量的全局源已由GlobalCrossTableTarget.RequireTarget正确抛NotSupportedException，旧测试硬收InvalidOperationException。

使用未改动的de5fea3固定正式程序集独立运行同一六个阴性用例，5通过／同一1失败，精确原因是“全局跨表目标只能使用固定条件，不能混入项目或父上下文变量。”。global-gate-baseline.log／TRX保存旧源码堆栈；未改冻结源或B120包。

仅校准global分支为精确NotSupportedException及完整原因，其余五个仍精确要求InvalidOperationException；六项都验证没有目标／staging残留。没有改成任意异常即通过，也没有修改产品全局范围、权限或Full门禁。最终六项全部通过，C93仅此范围完成，不表示全量CI已通过。

## 更新、保护与剩余范围

已有项目在元数据与数据库已经是double时，更新Data运行时并重新生成受影响的非冗余后端，按既有助手／CLI预览并应用；不需要新增DDL、回填、前端替换或手写迁移。若实际字段仍是decimal，改型仍必须由元数据及显式结构预览决定，不能通过本次升级偷偷改库。

回退须成套回退生成器及Data运行时；含double筛选的非冗余页将恢复旧生成拒绝，不承诺旧版本能继续生成新能力。固定实体写事务、文件引用、统计和既有权限继续沿原实现。

776份原页面／模板／元数据／生成产物及手写文件逐字核对0变化。当前仓库平台／示例元数据没有新增double字段，本任务没有手改或重新生成它们；只有专项样本新增／改型字段。未连接Server-200、未创建数据库对象；真实MySQL取值／SQL／CRUD、Excel／关系组合、浏览器、并发和性能仍待，A17／A23及MySQL首版整体不转记完成。

B120最终固定来源de5fea3、准备包、执行器及待批准单库范围保持；本修复不改写该既有冻结基线，也不能借其授权运行double实库用例。

后续A24已补普通与非冗余double列表统计的离线实现，见[数值合同、真实生成及剩余验收](../double-list-statistics.md)。此处原批次未支持统计的结论保留为历史边界，不将A24离线证据倒记为真实MySQL／浏览器或性能验收。
