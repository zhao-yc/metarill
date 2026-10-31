# 控件配置候选的固定只读投影

日期：2026-10-30；起点 `96de2d6`，本轮C62。保留原控件、接口、返回属性、排序和操作流程，只缩小基础设施候选读取的数据库列，不涉及新业务CRUD或元数据变更。

## 发现与实现

原 `ConfigurationCandidatesBusiness` 使用 `SqlRowStore.ReadAsync` 读取已发布的全部物理列。三个候选/继承用途实际只消费下表字段，其余列含页面SQL或大段控件配置，查询后即丢弃。

| 原用途 | 实际SELECT字段 | 保持的原行为 |
| --- | --- | --- |
| 表候选 | config_table的id、name | 认证项目、活动行；按数值ID倒序；原id/name/value/text，ID为字符串 |
| 页面候选 | 父表id；页面id、name、type、config_table_id | 原单选默认type1/多选type2；父表同项目且活动；按名称Ordinal及ID倒序 |
| 控件明细继承 | 字段config_table_id、control_kind、column_name、column_type、name、control_detail；父表id | 字段与父表项目检查；同一原事务；返回原五字段，control_detail原文含whereSql/orderBy不裁剪 |

新增 `SqlRowStore.ReadProjectedAsync`，仅接受服务端已发布物理列白名单中的非空、不重复固定列。字段和过滤名在数据库工作前检查，值继续经原驱动参数化。未知表/列、表达式、星号、大小写别名和取消均不能进入实际命令；没有新的HTTP参数允许客户端指定投影。

其余ReadAsync、含墓碑读取及ReadForUpdate继续完整读取，未把窄实体用于写事务、文件证明或初始化。投影仍用数据库适配器的引号/参数/连接边界，未在业务层新增数据库品牌判断。没有更改原查询的隔离级别、查询次数、过滤/排序实现或候选加载方式；不把此修改当作按用途List/Detail/Selection独立VO接线。

## 实际验证

| 验证 | 结果与范围 |
| --- | --- |
| 原始问题复现 | 在起点源码上运行候选专项9项，4失败/5通过/0跳过，失败直接显示实际SQL仍选择整行；原数据、过滤和返回断言已经通过。 |
| 最终元数据测试 | 严格构建464通过、25项条件跳过、0失败；新增18项覆盖真实业务/仓储命令、完整返回、长ID、名称与ID排序、项目和墓碑过滤、父表拒绝/同事务、默认/坏控件类型、投影输入和取消前检，以及原完整读取/写锁保持。 |
| 最终数据层 | 严格构建1222通过、24项条件跳过、0失败。 |
| 最终API | 严格构建87通过、49项条件跳过、0失败；既有候选真实MySQL用例仍属条件跳过，不计正式权限/存储通过。 |
| 完整管理包 | 4通过、0失败、0跳过：实际完整包从随包源码独立构建和原匿名入口验证；新的仓储/候选实现已进入分发闭包。 |
| 原前端与生成产物 | git diff核对frontend与backend/generated无变化；没有运行页面再生成，也不需要用UI改动配合。 |

测试采用现有ADO受控传输，实际执行生产业务和SqlRowStore，按真实SELECT列建立返回数据并记录参数/事务；不连接MySQL，不用该夹具证明真实锁、隔离或查询耗时。投影输入同步异常的测试曾误选xUnit异步重载导致严格编译失败，改为明确Action捕获同步前检后复验，没有关闭分析器；失败日志保留。

私有证据在 `E:\\git\\metarill\\.local`：candidate-projection-red.log、candidate-projection-green.log、candidate-projection-metadata-final.log（首轮编译失败）、candidate-projection-{metadata,data,api}-closed.log、candidate-projection-management-package.log。对应TRX在各测试项目TestResults；不提交这些日志/产物。

## 跟进与未完成

无需DDL、迁移、回填或页面再生成。完整管理工程使用原在线助手完整预览/同步时配套更新Data的SqlRowStore与Runtime的ConfigurationCandidatesBusiness，保留手写扩展；只更新业务文件而遗漏新增仓储方法会编译失败。回退时同样配对回退两个文件，不改变数据库。

此次仅证明固定候选减少SQL取列且原合同保持，未测量数据库传输量、真实内存或同条件延迟/吞吐；不声称性能已达标。原浏览器、普通身份/真实MySQL和其余配置工作台矩阵继续；V09用途VO/SQL与V08性能整体验收仍未完成。C61的固定S3来源、pending租约和精确独占桶授权保持独立，本次没有读取其凭据或执行远端资源操作。
