# 生成包发布失败诊断

本增量修复的是确定存在的取证缺陷：`FrontendPackager` 发布 stage 失败后，前端和外层 `ApplicationPackager` 的 `finally` 会相继删除现场。此前两个 `LocalGenerationUpdateTests` 场景出现 `Directory.Move` / `Access denied`，原 stage、错误码和持有句柄都没有保留。历史两次失败的实际持有者及原 HResult 仍未知，不能将这次复跑通过写成根因已经解决。

## 单次发布与保留边界

[PackagePublication](../backend/src/Metarill.Generation/PackagePublication.cs) 仍只尝试一次发布：目标若为空则删除，随后重命名同级 stage。非空目标拒绝，不覆盖、不更换 TMP、不增加自动重试，也不降级成逐文件复制。

只在该发布边界的真实 `IOException` / `UnauthorizedAccessException` 上抛出 `PackagePublicationException`，公开保留：

- 原异常对象作为 `InnerException`，其类型、原始 HResult、堆栈不会被诊断异常替代；外层 HResult 同样取原值。
- 明确的 `StageDirectory`、`OutputDirectory`、`ContainingStageDirectory` 和可用时的 `DiagnosticPath`。
- stage 内固定 `.metarill-publication-failure.json`：时间、当前 PID/进程名/CWD、发布路径、目录存在性/属性、原异常类型和真实 HResult。诊断写入失败仅附加错误类型/码，不覆盖原异常。

前端发布失败时，外层仅保留包含该真实失败 stage 的本次应用 stage，并另写外层诊断。两个 `finally` 都跳过这次失败目录，不在清理过程中覆盖原异常。普通校验、渲染或写文件失败沿用原清理边界；任意同名 marker 不能使目录得到保留。

报告不记录设置、文件正文、环境变量、命令行或连接信息。保留的 stage 是诊断候选，不能直接当作已经发布的应用。后续应先核对实际路径、归属和原错误，再决定重新生成或精确清理本次 stage；禁止批量清空 Temp。

## 测试进程取证

已知历史失败集中在 `LocalGenerationUpdateTests.Packages`。该 fixture 仅在捕获真实发布异常、且 stage 绝对路径属于自身随机 GUID 根目录时保留本次目录。没有修改全局 `TestDirectoryCleanup`，也不读取任意 marker 来决定清理。

Windows 测试发生这类异常时，[PublicationHandleProbe](../backend/tests/Metarill.Generation.Tests/PublicationHandleProbe.cs) 只同步检查本测试进程的磁盘文件句柄，只输出本 stage 下的匹配路径；外部/保护进程及瞬时关闭句柄明确为未知。系统句柄原始表、其它路径、命令行和文件内容不会落盘。全系统路径扫描曾在控制实验耗时约54秒，因此没有放入默认失败路径。

句柄匹配本身不提供原始共享模式证明。受控测试明确打开一个未授予 `FileShare.Delete` 的文件，在本机 Windows 真实复现目录移动失败，并从实际异常取得 `-2147024891`、由探针识别本进程精确文件。这证明该共享模式可以产生此类失败，不能据此将历史失败归因于当前进程、杀毒软件、浏览器或任何其它进程。

## 本轮验证（2026-10-19）

- 原源码冻结后，准确的四个历史场景 4/4 通过；这不是根因证据。
- 新增保护及原场景定向 8/8 通过，包含前端/外层非空目标竞态、原错误码和两层stage保留、任意marker不保留、真实Windows文件锁及受限句柄探针。
- 完整 `Metarill.Generation.Tests`：127项，125通过、0失败、2跳过。跳过的两项要求专属随机全新Server-200库，不能计作首次安装完成。真实MySQL业务、关系候选、文件引用和独立宿主测试按隔离设置实际执行。

本轮没有自然复现历史Access denied，因此没有新的历史故障现场或外部持有者证据；不进行无限重复碰运气。原失败TRX、私有阶段路径说明、控制实验和本轮完整TRX保留开发者 `.local`，不进入开源提交。没有更改Multi、模板、运行时资产清单、数据库结构、旧YZP或生产部署。
