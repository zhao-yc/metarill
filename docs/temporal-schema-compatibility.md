# 时间精度与原审计默认值（2026-10-29，A18）

本批解决同模型性能基线暴露的真实结构表示缺口，不提供性能成绩。原Java样板三表使用秒精度DATETIME，两个审计时间列各有now()默认；旧C#统一生成datetime(6)，转换器还清空了动态默认，形成9处类型精度、6处默认值差异。原有对比失败及冻结样板不改写。

## 元数据与适配边界

逻辑类型仍为datetime/time，不新增平台配置表字段。物理类型ColumnType可明确使用datetime(0)至datetime(6)、time(0)至time(6)。0生成MySQL裸datetime/time；没有明确精度的旧空值、裸datetime/time继续六位。旧时间字段ColumnLength实际含空串、50和255，本批继续忽略这些遗留长度，不把它们解释成精度，也不批量修改存量元数据。

类型推导使用固定完整语法，拒绝超范围、负数、双位精度、SQL尾句和逻辑时间种类冲突。物理元数据组合检查加入有效时间精度，不能把不同精度的同名基础列合并成“相同”。既有元数据、原页面、样式、控件和事件不修改；精度限定值的原配置编辑器及完整平台导入保存仍须现场验收。

MySQL规则参考[官方小数秒说明](https://dev.mysql.com/doc/refman/8.4/en/fractional-seconds.html)及[时间默认值/精度一致要求](https://dev.mysql.com/doc/refman/8.4/en/timestamp-initialization.html)。仅在MySQL适配器形成具体SQL，不据此宣布其它数据库已支持。

## 审计默认值及反查

仅原insert_time/update_time的datetime字段可声明精度匹配的now()/NOW(p)/CURRENT_TIMESTAMP(p)固定默认，同义词归一为CURRENT_TIMESTAMP或CURRENT_TIMESTAMP(p)。普通业务字段、delete_no、未知函数、表达式、尾句、精度不符仍拒绝；没有开放原生SQL默认值配置。

结构反查仅对这两个审计字段、精度匹配的固定默认且EXTRA恰为DEFAULT_GENERATED解除生成列保护。真正生成列、ON UPDATE及其它未知EXTRA不解除。反向草案用显式ColumnType保留裸时间的零精度，并交给真实正向编译器双向核验；不分配真实配置ID、不自动写元数据或DDL。原配置保存管线已有column_type/default_value字段，本批没有新增API参数或页面。

本A18批次的审计Service仍按当时DateTime.UtcNow生成；后续C30已从生成源头改为原宿主日历，见[生成审计专项](generated-audit-calendar.md)。两批均未改变客户端时钟、现有JSON格式或数据库会话时区。数据库默认只是原结构中已有的省略列回退语义，不能据离线DDL推断应用与数据库时钟相同。实际秒/微秒读写、舍入、SQL模式、时区及两侧请求结果尚未验收。

## 生成包与受控更新

原三表46列、6索引、3页面、3按钮、1主子关系全部保留。新时间样板完整包实际独立严格编译通过；同来源完整包的正常更新/重复零差异、原业务partial保护和原Vue逐字一致通过。没有删关系、附件、业务按钮或加虚假项目列使样板通过。

当前候选必须包含Core时间类型/物理组合以及Data结构编译/草案/反查五份实际源码；同时删源码和归属不能伪装成完整可更新包。旧target仍可预览运行时升级，不要求其事先包含新解释。源码缺失的六项阴性验证目标未写入；扩大相关批次33项通过，补齐物理零精度别名后最终完整包/缺源码7项复验通过，重叠项不累加。

## 同模型实际离线对照

冻结Java元数据和正式DDL仍来自b6d64fb样板；新转换器为9个时间列明确datetime(0)，保留6个审计now()，原ID/控件/关系/索引不变。真实MySqlSchemaPlanner在未创建DatabaseSession的情况下编译3表/46列/6索引和3条CREATE计划；新比较回执compiledComparison.offlineCompiledSchemaEquivalent=true、differences=[]，原15处差异在本新离线模型中归零。

只对两个原审计列、相同列/函数精度归一NOW和CURRENT_TIMESTAMP；类型、主键、可空、列集合、索引及其余默认仍逐项比较。不能将datetime与datetime(6)视作等价，也不对任意表达式宽松归一。本次比较覆盖列集合/类型/主键/可空/默认和索引，不证明实际表/列字符集与排序规则、引擎默认、SQL模式或时区一致。samePhysicalSchemaProven、databaseInspected及performanceAccepted仍为false，没有建表、数据库反查或负载。本次回执检查脚本首轮误读compileComparison键，实际键为compiledComparison；只修正读取并核对原回执，没有重跑编译、覆盖旧基线或修改差异结果。

私有目录为 `.local/temporal-definition-20261029`：fixture/comparison及comparison-terminal新元数据与转换回执、两次真实physical-plan及测试TRX/日志；最后物理零精度别名规范修正后重新编译的计划与前次逐字内容一致，最终final-receipt.json关联全部TRX和来源稳定核验。旧paired-baseline和B98已冻结包/租约保持原样；新模型不自动替换B98正式包或已授权对象。

## 验证与剩余

- 结构精度/默认/反向草案、原结构测试共85通过，零跳过。
- Data严格全量1102通过、22项缺实库配置跳过；Metadata严格全量351通过、25项缺实库配置跳过，均零失败。
- 性能转换/回放工具28通过；不是性能负载。
- 原完整样板、打包/全局来源/更新相关33项通过；最后物理别名修正后的完整包及缺源码7项通过，零跳过，不累加为40项。
- API69通过/47实库条件跳过、Agent351/25、兼容18/11，均零失败；CLI严格构建零警告零错误。
- 899份本批后端代码/测试/性能工具来源在最终验证内未变；215既有产物两轮逐字稳定，包含24手写partial，492原前端文件内容未改。

真实MySQL安装/反查/导入保存、精度读写、原配置页、正式双端功能/SQL/数据/资源及性能仍待完成，A18、V08/V14和MySQL首版整体不标完成。

## 既有项目跟进与回退

已有项目若不采用显式精度或审计默认，无需改元数据、SQL、回填或再生成。采用时先成套更新Core/Data与受控更新器，审核元数据及实际库结构，重新取得结构预览计划，再按原预览→执行→核验流程处理；不能只复制一份新Data文件或手改生成实体。若涉及新生成合同则配套重生成后端，前端没有页面变更步骤。

已有列精度变化仍不自动执行，尤其六位降到零位会丢失信息，现有保护继续拒绝；没有为基线绕过这一门槛。回退须复核原元数据/运行时与现有实际结构，不能只退DLL让显式零精度变回六位，也不自动回滚数据或已执行DDL。
