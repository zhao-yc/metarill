# V755 固定候选 Windows 平台回归

2026-11-09。固定 4ef3ca76 的 2,957 份 Git 源码，与公开候选构建的源码摘要逐项一致。使用本机固定 SDK、缓存依赖和独占短临时目录。

| 后端套件 | 通过 | 条件跳过 |
| --- | ---: | ---: |
| Metarill.Agent.Tests | 447 | 25 |
| Metarill.Api.Tests | 235 | 49 |
| Metarill.Compatibility.Tests | 19 | 11 |
| Metarill.Data.Tests | 1602 | 32 |
| Metarill.Exports.Tests | 36 | 0 |
| Metarill.Files.Tests | 261 | 60 |
| Metarill.Mcp.Tests | 30 | 0 |
| Metarill.Metadata.Tests | 577 | 41 |
| Metarill.Security.Tests | 175 | 1 |

C# SQL 观察器的 34 项通过。空库租约、TLS、S3、公开包和性能工具的 481 项通过。原前端运行时、页面请求、助手浏览器协议和元数据派生检查均通过。平台与示例重新生成及同步后的 717 份文件逐字一致。

首次运行 Agent CLI 时复用了 180 秒外层预算，测试进行中被终止。原日志保留，超时试跑的独占临时目录已核清。用同一份冻结源码在独占短路径以 720 秒预算复验，全部 141 项通过，实际耗时 367.6 秒。

此次为本机平台回归。Windows 六组完整生成、GitHub Actions、公开 ZIP 的实库安装和真实浏览器分别验收。
