# V930／V931 当前候选 Linux 生成回归

2026-11-10。固定产品 `68af5319148009f1a5484d2cbd1383b9e7b71ed2`。在 Server-200 使用禁网容器和离线依赖。3,058 份源码逐文件核对，77 个 NuGet 包和 28 个 npm 包按固定摘要传入，Linux x64 SDK 10.0.402 按微软发布的 SHA-512 校验。Node.js v24.14.0 离线安装依赖成功，.NET 测试项目还原及构建成功，零警告、零错误。未分组发现 3,150 个用例。

| 轮次 | 实际执行 | 结果 |
| --- | --- | --- |
| V930 | 按 CI 过滤器顺序启动第 1 组。 | 记录一项失败，`RuleRecordNativePreparationTests.Full_rule_record_package_is_fixed_before_native_lease` 的包内构建子进程非零退出。该断言未输出子进程日志。整个容器达到 1,800 秒上限，第 1 组未生成 TRX，第 2 至 6 组未启动。 |
| V931 | 同一固定输入重新传入独占范围，准备单独执行该用例并私下保存构建日志。 | Server-200 当时的可用内存低于 3 GiB 启动线，执行器在创建容器前停止。定向用例待运行。 |

V930 容器配置为 4 CPU、6 GiB、最多 256 个进程或线程。运行中采集的 cgroup 计数显示进程上限命中一次，内存 OOM 与 OOM kill 计数均为零。无法从该聚合计数将上限命中归因到失败子进程。两轮隔离目录和容器均经新 SSH 独立核清。

同一候选的 [V921 Windows 六组回归](current-windows-generation-full-v921.md) 已通过。核对时该候选的 15 项 GitHub 作业仍在排队。Linux 六组以及 MySQL 首版发布门槛继续待验。[当前 GitHub Actions](https://github.com/zhao-yc/metarill/actions/runs/42747715116)
