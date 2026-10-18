# 离线代码生成

执行：`dotnet run --project backend/src/Metarill.Cli -- generate --metadata backend/metadata/demo.json --output backend/generated/demo --namespace Metarill.Demo`。

生成目录需要在宿主项目中包含所有 `.cs`，并引用 `Metarill.Core`、`Metarill.Data` 和 `Microsoft.AspNetCore.App`。宿主注册真实 `IRuntimeAuthorization`、`IIdGenerator`、`DatabaseSession`，然后调用生成命名空间的 `GeneratedRegistration.AddGeneratedServices`。若平台与业务库不同，传入返回宿主管理会话的 `databaseFactory`；同一生成命名空间的 Mapper、Service 始终共享该会话。

- `.g.cs`、生成 Vue 和清单归生成器所有。`.cs` 手写 Service 仅首次创建；可实现 `BeforeSave`、`BeforeDelete` partial 钩子。派生字段应通过 `submittedFields.Add("数据库列名")` 明确加入更新。
- 普通写入经过页级编辑白名单。省略字段不修改，显式 null 清空；未知字段及系统字段拒绝绑定。审计字段来自真实 actor。
- `IRecordMutationObserver` 在真实事务提交前执行，异常回滚；文件引用及平台联动可加入同一事务。新增传实际完整实体值，更新传实际已更新值和 id，删除传全实体及删除标志，键为数据库列名。
- 查询范围只接受明确列与身份/父记录参数的等值 AND 条件；其他原生 SQL 拒绝生成，不会静默扩大访问。筛选/排序限定开放字段，空筛选与空排序保持原 YZP 行为。
- 生成器先完成全部元数据、路径、归属、手改与冲突检查，才写入文件。`.metarill-generation.json` 保留摘要，不含连接信息；失败回滚触碰过的原文件。输出禁止通过目录联接或符号链接。
- 前端模板复用现有 `common/yzp`、uni-ui、文件库和定制组件，生成 `pages.generated.json` 供工程集成；不覆盖宿主自有 `pages.json`、应用入口或运行时。
- `generation-capabilities.json` 和 CLI 警告明确列出未接入的控件及关系，前端按只读保护。特殊 SQL、函数表、视图、复杂权限、聚合统计及定制导入不因代码编译成功而视为已支持；需逐项实现和真实验收。

回归命令：`dotnet test backend/tests/Metarill.Generation.Tests`。测试包含真实生成产物引用 Data 编译、JSON 字段存在性/数组协议和授权拒绝；不连接本机数据库。数据库 CRUD、事务、SQL 性能及多端设备验收由独立集成测试完成。

## 复制、导入与导出

- 复制沿用 `POST copy/{id}`，检查独立复制权限，保留业务值但重建主键与审计，不复制子表；观察者按新记录创建执行。
- 普通导入沿用 `POST importDatas{pageId}` 和 `{requestJson:[{中文标题:单元格值}],parentId}`。未知列、系统列、只读列和Excel覆盖父键均拒绝；常量、日期及文件JSON数组规范化后复用标准Create/Update规则。全批次同事务，错误附行列。`OverWriteByFirstColumn`只在当前页面范围内唯一匹配，不会跨项目覆盖。
- 明确的单值或多值关联在新增、修改、复制、导入前检查目标存在、逻辑删除及项目归属；修改只检查实际提交的关系字段。仅显式global元数据可跨当前项目范围，不接受请求参数放宽。
- 导出沿用 `POST initExport` 返回票据。生成方法复用范围和筛选、固定list/edit投影，关系名称批量补全，64位整数输出文本；宿主实现 `IPageExportService` 负责Excel资源和 `/admin/sys/export/export?exportId=...` 下载。
- 定制导入及尚无安全候选规则的复杂关系会明确拒绝，不会退回裸表查询。数据库整批回滚及Excel票据下载需要在集成服务器继续验收。

## 独立应用包

执行：`dotnet run --project backend/src/Metarill.Cli -- package --metadata backend/metadata/demo.json --output .local/demo-application --namespace Example.Demo --source-root .`。

`ApplicationPackager.Create(catalog, outputDirectory, rootNamespace, sourceRoot)` 返回 `ApplicationPackageResult`（Files、MetadataSha256、RedactedPaths、Warnings）。HTTP 宿主必须由管理员配置可信 sourceRoot，不能让请求任意指定服务器源码路径。输出目录必须不存在或为空，失败清理临时目录，不覆盖既有项目。

包包含独立 ASP.NET Core 工程、Core/Data 源码、元数据、生成代码、固定 SDK 和直接依赖版本；首次 restore 生成传递依赖锁文件。仅复制允许的源码/工程，不复制 `.local`、bin/obj、配置或凭据。已知敏感配置键递归剔除，密码等敏感列保留定义并清空默认值。所有文本与归属 JSON 固定 LF；相同输入重复打包逐字节一致，跨操作系统仍需 CI 对照验证。

默认拒绝匿名 API。仅 Development 环境显式配置账号密码可启用开发会话，项目由包内元数据固定；正式认证通过同程序集 HostCustomization partial 替换。正常启动不执行 DDL，结构预览/执行必须使用显式命令。文件、导出下载、Agent、完整 uni-app 工程不随包提供，不能宣称这些能力已经独立化。

本轮已用独立业务测试库验证包内宿主的 CRUD、复制、部分更新、系统字段拒绝、服务端项目隔离、退出和到期会话失效；显式结构预览无差异且未执行 DDL。自动测试额外验证完整包从自身 Core/Data 工程编译、敏感值剔除和原子打包；这些结果不代表其他数据库和前端设备验收。

导出 Controller/Service 需与新的 Core/Runtime 同步再生成：Controller 将延迟准备委托交给 `IPageExportService`；服务端传入行数上限，SQL最多读取上限加一条并显式拒绝超限，不静默截断。自定义导出实现未接入新重载时默认拒绝，不提前读取数据库。
