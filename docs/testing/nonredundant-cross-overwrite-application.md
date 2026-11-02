# R0跨表覆盖导入：完整源码与Production/Web装载（B125）

日期2026-11-02，起点e9ce459。承接B124内部逻辑匹配，本项验证此前缺少的自身完整运行时／原前端／正式Web装载；不修改生成器、产品元数据、原页面／组件或公开Full策略，不连接MySQL，不消费V37或其它实库授权。

## 六模型与包内代码

沿B123原六模型目录，只有测试夹具明确把六个cross_consumer页设为OverWriteByFirstColumn，并将其原CascadeTree字段设为覆盖首列。原表结构、关系语义、页面列及其它普通导入页不改。六页均生成非空逻辑匹配计划；关闭Excel后的基底通过全部公开Full能力审计，实际覆盖模型仍被公开Frontend／Application Full拒绝。

通过原CrossTableExcelApplicationPreparer生成内部完整源码目录，核全文件标记摘要、实际元数据等值、不可发布／web-database-v1标记、自带所有基础项目和每个ProjectReference均为包内相对存在路径。先严格编译原生产完整源码，再只加入HostedService观察器二次严格编译；没有借工作区DLL替代Core／Data／Runtime，也没有替换Controller、身份、权限、事务或SQL。

## 实际验证

最终定向2项通过／0失败／0跳过：矩阵／门槛1项，自身源码与生产宿主1项。后者实际观察32个生成Service全部DI构造、32个原Import路由以及6个私有ImportMatchPlan；输出均只有主键及所需逻辑匹配字段，无附件数组，包含int64／string两种关系ID类型。RuntimeAuthorization与SecurityIdentityAccessor复用同实例。

Production／Web、关闭DevelopmentBootstrap、虚构MySQL 127.0.0.1:1配置下，真实宿主启动／health通过，六个原importDatas页路由POST均返回401；没有提供账号、会话或假数据库实现，不把未认证结果当普通身份写入通过。观察器仅读类型／计划／MVC／DI，不建立连接。当前只核对CascadeTree组件、远程候选运行时及Agent授权页三份原共享前端内容保持，不能替代全Web／跨端保真验收。

六模型完整元数据、包内文件摘要与生产安全绑定由原准备器保留；两轮编译中的第二轮含只读观察器，并非宣称未加入任何诊断源码。正规再生成保留一份实际标记的手写Service扩展，实际模型公开Full仍拒绝，不能把准备目录交付为可用完整应用。

## 失败保留与边界

首轮1过／1败：新增测试错误地对所有普通导入页直接读取可省略的importMethod，GetString遇到Undefined失败。只修测试的TryGetValue／ValueKind检查，六个覆盖页精确数量、私有计划与原32路由均保留；不是把32路由预期降成6，也不改产品。第二轮2过／0败／0跳过，与首轮矩阵测试重叠，不相加。

原始TRX及两组私有证据在.local/cross-overwrite-full-20261102；成功观察回执位于edae4e5ab36d4c5ba89632b17572a520。测试临时完整源码目录按既有边界清理，日志保留；没有真实数据库DDL／DML、账号、文件引用、覆盖写入、Excel工作簿、并发、普通身份或性能验收。

后续正式验收仍须独立固定实际包和受限测试范围，证明同项目／父范围逻辑匹配、唯一／歧义、稀疏上下文、原XLSX覆盖、整批DML／文件回滚、旧快照及并发。公开覆盖导入Full门槛保持，B14／B124／MySQL首版总验收不因本项关闭。

## 复核

```powershell
$env:DOTNET_HOST_PATH='E:\git\metarill\.tools\dotnet\dotnet.exe'
.tools/dotnet/dotnet.exe test backend/tests/Metarill.Generation.Tests -c Release --nologo --no-restore --disable-build-servers --verbosity quiet -warnaserror --filter 'FullyQualifiedName~NonRedundantCrossOverwriteApplicationTests'
```

本任务只有测试和文档变更，无需DDL、回填、产品再生成或运行时下发；V37仍使用其已固定7cc3a77归档及原三镜像，不包含本文件的后续观察器夹具。
