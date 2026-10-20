# 生成测试的共享源码构建预算

真实 Generation 回归出现 `CS2012`：`GeneratedMutationGuardFixture.InitializeAsync` 经 `BuildGenerated` 构建临时项目时，根 `Metarill.Data/obj/Debug/net10.0/Metarill.Data.dll` 被另一进程占用。原始日志保留在主工作区私有 `.local/manager-name-full-generation-integrated.log`，没有取得当时文件句柄，不能据此认定某个具体测试就是持锁者。

临时项目具有唯一名称和输出目录，但 `ProjectReference` 仍共用源码树下 Core/Data 的中间产物。现有多个未标记非并行集合的测试类调用同一个 `BuildGenerated`；新增账号凭据测试也增加了一路调用。旧代码已存在可竞争窗口，本次不能仅靠一次报错断言旧版本曾经发生过同样失败。`--disable-build-servers` 和唯一的最终 `--output` 都不隔离依赖项目 `obj`。

## 最小修复

测试进程内共用 `SharedSourceBuildGate.Shared`，仅串行以下五个实际构建入口：

- `GeneratedRelationOptionsMySqlTests.BuildGenerated`：所有调用者共用 Core/Data。
- `GeneratedContractTests`：手工 `Contract.csproj` 引用同根 Core/Data。
- `GeneratedMutationGuardMySqlTests`：手工 `Guard.csproj` 引用同根 Core/Data。
- `LocalGenerationUpdateTests.Cli`：仅 `build` 命令，三个真实 CLI 构建调用。
- `PackageCliTests.RunAsync`：仅 `build` 命令。

门闩覆盖子进程启动、退出和输出读取；构建中断后只停止调用方自己的进程树，实际等待进程退出并排空两条输出管道后再释放租约，不能把 Kill 返回当作退出完成。停止失败也等待原进程退出，不重试构建；清理异常与原始中断一并保留。排队在原 60/90 秒构建预算之外。非构建 CLI 命令、生成、程序集读取和普通合同断言保持原并行，不增加重试、超时、跳过或程序集全局串行。其它 Full 包构建引用包内复制源码，现有独立宿主和实库集合策略保持不变。本门闩只协调同一个测试进程，不承诺协调人工并发构建、其它测试进程或第三方扫描进程。

## 验证边界

新增三项无数据库测试覆盖真实自有 Node 子进程的终止与输出排空、排队互斥、普通命令不等待、异常释放、重复释放保护以及等待取消后仍可继续。此源代码准备期间主工作区完整回归仍运行，按约定未叠加任何构建或测试；待主回归结束后再执行这三项、真实生成编译和完整 Generation 验证。不得把静态修复或偶然一次通过称为已定位当次持锁进程。
