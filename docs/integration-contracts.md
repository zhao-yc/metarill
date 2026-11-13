# 并行实现集成契约

- Core 为 backend/src/Metarill.Core；元数据对象见 Metadata/MetadataCatalog.cs，兼容 YZP 的 configTables/configColumns/configPages/configPageColumns 命名，扩展字段在 Extra 保留。
- 数据访问项目为 Metarill.Data，命名空间 Metarill.Data。DatabaseOptions 含 Provider 与 ConnectionString；DatabaseSession 提供 Orm(IFreeSql)、Select<T>/Insert<T>/Update<T>/Delete<T>（自动附加当前事务）、InTransactionAsync<T>(Func<Task<T>>,CancellationToken)。Orm 全局禁止自动改表。
- 数据结构服务 SchemaService 提供 PreviewAsync(MetadataCatalog,CancellationToken) 和 ApplyAsync(SchemaChangePlan,CancellationToken)。MySqlDatabaseProvider 为首个实现。结构同步限制在元数据定义的表，失败不伪装事务回滚。
- 生成项目 Metarill.Generation，依赖 Core。CSharpGenerator.Generate(MetadataCatalog, string outputDirectory, string rootNamespace) 返回 GenerationResult；离线控制台项目 Metarill.Cli 调用它。输出基于目标项目中的 Core/Data 及 ASP.NET Core 引用，先支持一个完整普通表示例并持续扩展。
- 生成 Entity/DTO/VO/Mapper/Service/Controller，生成空间 <Root>.Generated.*，手写服务在同命名空间 partial，手写文件首次创建，Service 注入 DatabaseSession 与 IIdGenerator；Controller 注入 IRuntimeAuthorization，先校验页面权限。
- 标准 Controller 路径 /api/admin/auto/{tableName}/{pageId}。尽量兼容已有 YZP 方式，请参考原 Controller 实际方法；PageRequest/R/PageResult 位于 Core.Contracts。生成命令不能执行 SQL。
- 元数据唯一来源：backend/metadata/platform.json（脱敏通用平台定义）与 backend/metadata/demo.json（独立客户/订单等虚构示例）。前端和基线任务先整理 platform-source.json，平台元数据生成由协调者集成。
- 任何任务不修改其他任务拥有的目录，不添加凭据。独立普通克隆和 codex/ 分支，先本地提交，协调者拉取集成；禁止工作树。
