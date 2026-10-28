# 同模型 Java／C# 性能基线准备

2026-10-28：固定Java样板的正式两轮生成、独立JAR及类路径检查已通过；同一模型的C#完整包仍被真实全局来源门槛拒绝，尚未构成可运行的双端基线。没有为获得成绩删字段/关系/附件/主子/按钮、改成项目表或关闭生成门槛。本项不代表性能验收，V08仍未通过。

## 固定来源与真实结果

| 项目 | 实际证据 | 仍缺 |
| --- | --- | --- |
| Java来源 | YZP `b6d64fb2ad4ac25b004eeaa326092de3cfb7f311` 独立普通克隆；现有V8SampleGenerationTest和JSON样板，未修改旧Java工作区 | 不是平台在线实例的性能成绩 |
| Java正规生成 | 1项JUnit通过、零失败/零跳过，12.06秒；两轮受管产物稳定、手写负金额校验保留，真实生成DDL和元数据回执 | 没有数据库连接或真实CRUD |
| 独立Java宿主 | 同源prepare-host装配35个明确来源，JDK21.0.12/Maven3.6.3/Spring Boot3.5.14独立编译打包；JAR 103135232字节，类路径单测1/1零跳过，0.100秒 | 未启动Spring或执行登录/服务请求；不以编译替代功能 |
| C#固定来源 | Metarill `498712887a79e7df41ddee55daa0d284461ad24c`，独立克隆复现生成拒绝；实际编译时源码/程序集摘要及该提交归档保留 | 后续账号菜单提交22344e7由另一任务交付，本次仅同步保留，不能将它改记为本轮编译基线 |
| 同模型夹具 | 原3张R1表、11配置字段、3页、3按钮、1主子关系全部保留；按正式Java DDL补35个隐式声明，最终46列、6原二级索引 | 账号/会话基础设施两侧不同，原业务扩展须对照移植；不等同完整Runtime A |
| C#物理编译 | 真实MySqlSchemaPlanner离线生成3表/46列/6索引及3条CREATE计划，未创建DatabaseSession、未执行DDL | 离线计划不代表已安装数据库或完整应用可生成 |
| 结构比对 | 列集合、主键、可空及6索引一致；9处datetime→datetime(6)，6处now()默认未在C#元数据表达，共15处差异 | 不能宣称同物理结构；须审查共同实验DDL、应用写入时钟及两侧读取精度 |
| 完整包门槛 | 原无项目列关系来源被拒绝；固定4987128及原样本保留，全局来源B98待补 | 不能删关系、加虚假项目列或绕过RequireCached/发布证明来代替移植 |

Java第一次 `-o` 未运行测试：本地缺少Surefire插件的plexus-utils 1.1依赖。补齐固定公开依赖后同一来源正式生成通过，离线失败单独保留；没有升级库版本或修改Java源码。C#首轮未显式声明全局范围被拒绝；仅依据正式DDL确实无项目列明确scopeKind后，进一步暴露全局来源门槛。最初在共享工作区看到的拒绝已在独立、固定4987128的干净产品来源中再次复现，不借用另一任务的暂存产品改动作为证据。

## 离线转换工具

`tools/performance/prepare_v8_baseline.py` 仅支持现有V8离线样板JSON与其正式CREATE语法，不是旧YZP项目迁移器、SQL执行器、通用SQL解析器或生产初始化入口。

- 固定保留原十进制字符串标识、默认值展开、全部页面/控件/按钮/子页、条件/必填、List名称与目标。只在目标页唯一时建立明确链接，冲突或多页不猜权限。
- 真实DDL补主键、审计、软删及名称缓存物理列；新夹具字段用稳定独立ID，不改原配置字段ID。不按_id后缀把原varchar关系改为bigint。
- 只有DDL确实有yz_project_id才声明project；无项目列的本样板明确global，仍受C#既有全局来源门槛约束，不能靠标签取得执行许可。
- 校验原类型/长度/非空、原字段归属、页面与字段所属表及表集合。未知DDL、非CREATE、重复表/列、缺主键、ID浮点化/超限和冲突拒绝，不以源头覆盖差异。
- 保留原Java的index_id唯一索引和index_delete_no；index_id虽与主键重复，这轮不单边删除改变成本。后续优化需两端同样结构另作对照，不将不同索引下的数据当同条件。
- 可传真实C#离线方言计划，逐表/列/主键/默认/索引比对；仅将bigint(20)显示宽度视为bigint等价，时间精度和动态默认明确不同。相同离线计划也不能证明实际SQL、数据或数据库实例一致。
- 不覆盖已有输出目录，不读连接配置，不启动任何宿主、网络、Docker或数据库。

```powershell
# 在固定Java源码根目录：现有正式生成入口，只用冻结JSON，不启动Spring。
mvn -f backend/pom.xml -Dtest=V8SampleGenerationTest -Dyzp.sample.outputDir=<全新绝对输出目录> test
node tools/acceptance/prepare-host.mjs <上述输出目录>
mvn -f <上述输出目录>/backend/pom.xml -DskipTests package
mvn -f <上述输出目录>/backend/pom.xml -Dtest=SampleClasspathTest test

# 在固定Metarill源码：仅写新夹具目录。
python tools/performance/prepare_v8_baseline.py --java-metadata <Java输出>/metadata.json --java-schema <Java输出>/schema.sql --output <新C#夹具目录>
# 可加 --csharp-plan <真实MySqlSchemaPlanner离线结果.json>，记录编译差异。
python -m unittest discover -s tools/performance -p 'test_*.py'
```

C#物理核对使用真实Metarill.Data/MySqlSchemaPlanner.Compile及Compare(empty,desired)，输出包含offline:true、ddlExecuted:false和schema；没有自行重写类型映射。正式C# CLI仍以同一完整夹具调用 `package --metadata ... --output ... --namespace ... --source-root ...`，本次拒绝保持，不能用BackendOnly或删能力把它记作FullApplication成功。

## 全局来源缺口 B98

原Java三张样板表均没有yz_project_id，SampleContract→SampleCustomer及SampleContractLine→SampleContract使用物理R1缓存关系，后者页还保留真实parent_id范围。当前GlobalSingleRelationTarget.RequireCached沿用已有证明边界：来源必须有真实项目列，且不得声明global，因此该原样本未被覆盖。

后续必须在元数据、生成期证明、SQL范围、候选/已选读取、标准保存、名称传播/回填、父上下文、文件引用、权限及回滚整条路径证明这一原行为；表无项目列不代表任意跨项目可读写。保留现有拒绝，直到范围与当前身份可证明。还需移植原手写负金额规则及对照原响应；实际功能、同数据/SQL/索引/资源和性能都不可跳过。相关新增/修复需独立验收样本及精确清理范围，不能复用A15/C12等已结束租约。

## 本轮边界与证据

新增转换/结构比较9项，加既有回放18项合计27项本机回归通过，零失败；含实际回环HTTP，SSH/Docker/MySQL均未执行。CI改为发现两个performance测试文件，远端结果另核。Java两项JUnit属于两个独立过程，不能与27项相加为首版全量。

私有证据位于 `E:\git\metarill\.local\paired-baseline-20261028`：Java固定克隆、正式generation-report/两轮/手写/DDL、独立宿主及类路径报告、C#原失败/显式范围失败/固定498复现、46列物理编译计划、最终转换与15差异、源码/程序集摘要及归档。最终夹具metadata与实际用于编译及固定拒绝复现的v2逐字相同，回执有SHA。

本次在独立普通克隆工作，原工作区另一任务的已暂存账号菜单改动未由本任务暂存或提交；其推送22344e7后安全快进保留。没有改产品生成器/运行时/前端，没有建库/账号/容器，没有性能负载、跨端或生产部署。MySQL首版及后续数据库目标继续，不合main。
