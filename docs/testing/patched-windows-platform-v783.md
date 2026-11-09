# V783 修复后候选 Windows 平台回归

2026-11-09。由开发提交 74411104 提取 2,985 份 Git 文件。与已安装的 Linux 候选 a88bbc89 对照，运行时代码、前端和测试逐份一致，差异只在验收文档。使用固定 .NET SDK、缓存依赖及独占短临时目录。

| 套件 | 通过 | 条件跳过 |
| --- | ---: | ---: |
| Metarill.Agent.Tests | 447 | 25 |
| Metarill.Api.Tests | 240 | 49 |
| Metarill.Compatibility.Tests | 19 | 11 |
| Metarill.Data.Tests | 1602 | 32 |
| Metarill.Exports.Tests | 36 | 0 |
| Metarill.Files.Tests | 261 | 60 |
| Metarill.Mcp.Tests | 30 | 0 |
| Metarill.Metadata.Tests | 577 | 41 |
| Metarill.Security.Tests | 175 | 1 |

原前端运行时、Agent CLI 141 项、页面请求、助手预览传输和元数据派生检查通过。C# SQL 观察器 34 项，发行校验、空库与 TLS、S3、性能工具 481 项通过。平台和示例重新生成及同步后，717 份受管文件逐字一致。测试临时目录已独立核清。

本轮为 Windows 本机执行。完整生成六组此前已用旧基线执行，修复后的 GitHub CI 和正式性能矩阵各按实际运行单独跟踪。
