# 离线代码生成

执行：`dotnet run --project backend/src/Metarill.Cli -- generate --metadata backend/metadata/demo.json --output backend/generated/demo --namespace Metarill.Demo`。

生成目录需要在宿主项目中包含所有 `.cs`，并引用 `Metarill.Core`、`Metarill.Data` 和 `Microsoft.AspNetCore.App`。宿主注册真实 `IRuntimeAuthorization`、`IIdGenerator`、`DatabaseSession`，然后调用生成命名空间的 `GeneratedRegistration.AddGeneratedServices`。若平台与业务库不同，传入返回宿主管理会话的 `databaseFactory`；同一生成命名空间的 Mapper、Service 始终共享该会话。

- `.g.cs`、生成 Vue 和清单归生成器所有。`.cs` 手写 Service 仅首次创建；可实现 `BeforeSave`、`BeforeDelete` partial 钩子。派生字段应通过 `submittedFields.Add("数据库列名")` 明确加入更新。
- 普通写入经过页级编辑白名单。省略字段不修改，显式 null 清空；未知字段及系统字段拒绝绑定。审计字段来自真实 actor。
- `IRecordMutationObserver` 在真实事务提交前执行，异常回滚；文件引用及平台联动可加入同一事务。新增传实际完整实体值，更新传实际已更新值和 id，删除传全实体及删除标志，键为数据库列名。
- 查询范围只接受明确列与身份/父记录参数的等值 AND 条件；其他原生 SQL 拒绝生成，不会静默扩大访问。筛选/排序限定开放字段，空筛选与空排序保持原 YZP 行为。
- 生成器先完成全部元数据、路径、归属、手改与冲突检查，才写入文件。`.metarill-generation.json` 保留摘要，不含连接信息；失败回滚触碰过的原文件。输出禁止通过目录联接或符号链接。
- 前端模板复用现有 `common/yzp`、uni-ui、文件库和定制组件，生成 `pages.generated.json` 供工程集成；不覆盖宿主自有 `pages.json`、应用入口或运行时。
- `generation-capabilities.json` 和 CLI 警告明确列出未接入的控件及关系，前端按只读保护。特殊 SQL、函数表、视图、复杂权限、聚合统计及导入导出不因代码编译成功而视为已支持；需逐项实现和真实验收。

回归命令：`dotnet test backend/tests/Metarill.Generation.Tests`。测试包含真实生成产物引用 Data 编译、JSON 字段存在性/数组协议和授权拒绝；不连接本机数据库。数据库 CRUD、事务、SQL 性能及多端设备验收由独立集成测试完成。
