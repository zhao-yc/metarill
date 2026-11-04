# 原三表关系索引验收工具

本工具只服务 Java／C# 同条件性能复测的独占合成环境，不能用于生产部署或替代平台工作台。原六初始化保持；协调器已有独立接线；V162 已完成两端真实索引升级和固定客户七步写入复测，原失败及未覆盖负载保留。离线工具通过本身不代表实库或性能验收。

工具读取候选自身 `metadata/project.json`，按原包 SHA256 `12bc9e1ffa6cecccd78d4adca78a671064ed9f01607f90a46d10160fabe71ec2` 核验原模型，克隆元数据后只追加两条普通索引及六个字段引用。DDL 由 `MySqlSchemaPlanner`／`SchemaService` 生成：

| 表 | 索引 | 列顺序 |
| --- | --- | --- |
| sample_contract | ix_sample_contract_customer | sample_customer_id、delete_no、id |
| sample_contract_line | ix_sample_contract_line_contract | sample_contract_id、delete_no、id |

动作只有 `offline`、`preview-java`／`preview-csharp`、`apply-java`／`apply-csharp`、`verify-java`／`verify-csharp`。不接受 SQL、文件路径、任意数据库或账号参数。

在线入口要求协调器先验证同轮资源归属，再通过环境传入 `METARILL_INDEX_RUN_ID`、`METARILL_INDEX_AUTHORIZATION=approved` 及该轮已有的 `Database__ExpectedDatabase`／`Database__ConnectionString`。连接限 Server-200 的固定地址、3306、本轮 `mtf_<前16位>` 临时账号及两库；MySQL 实例 UUID 再由结构服务核验。私有连接不输出，也不能放在命令行或提交。

先预览；应用时 `METARILL_INDEX_EXPECTED_PLAN` 必须等于预览返回的 `planSha256`，绑定数据库、前后结构与真实变更。新进程重新预览并比对摘要，执行仍走 `SchemaService` 的原签名、目标及结构漂移校验。只允许精确两项增索引，验证动作必须零差异；不能把部分成功或其它DDL算作通过。MySQL DDL不能整体回滚，失败后应保留已成功范围，由协调器清理同轮独占库。

发布使用 `MetarillObservationDataAssembly` 显式引用同一候选的 Data/Core。镜像启动工具时通过 `dotnet exec --runtimeconfig /app/Application.Host.runtimeconfig.json --depsfile /app/Application.Host.deps.json /app/Metarill.Paired.Indexes.dll <固定动作>` 复用原宿主完整依赖闭包，只需额外工具 DLL/PDB，不替换依赖或启动 Web 宿主。

CI 回归位于 `tools/performance/csharp-host.Tests/RelationIndexProfileTests.cs`，真实原模型离线预览另有固定候选载荷证据；两者分别记录，不当作实库通过。


独立原生用途为 `paired-native-indexed-write-measurement-v1`，必须显式指定 `relationIndexProfile=original-three-table-relation-indexes-v1`、完整 8624 请求预算及固定 52 文件镜像。旧读写用途仍要求原三表索引，不接收这个升级字段。原六初始化完成并核清短期容器后，两端分别执行预览、应用和零差异验证；六个新增短进程也登记在同轮归属账本，负载之前逐属性核对两端索引，负载之后连同原数据／墓碑再次回读。

此配置只用于可审查的两端同条件优化。现有项目如需采用，应在索引工作台保存相同元数据后显式预览／同步；本工具不会让所有项目自动新增索引，也不删除旧索引或改任何字段、请求、权限与事务语义。
