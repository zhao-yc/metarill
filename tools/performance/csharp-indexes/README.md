# 原三表关系索引验收工具

本工具只服务 Java／C# 同条件性能复测的独占合成环境，不能用于生产部署或替代平台工作台。原六初始化保持；协调器接线与正式实库升级尚未完成，离线通过不代表已经执行 DDL 或性能验收。

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
