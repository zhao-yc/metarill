# V787 修复后 Linux 发行包复现

2026-11-09。用提交 `a88bbc891cca527e8211fdbf1bcad9428a64af6b` 对应的公开构建脚本、固定 .NET SDK 和本机缓存，从独立输出目录重建平台与独立 Full Linux x64 ZIP。两个包均通过公开发行工具的清单校验，与 V778 在 Server-200 实际安装验收的原 ZIP 逐字一致，SHA256SUMS 也相同。

| 包 | 来源文件 | 宿主文件 | ZIP 字节 | SHA-256 |
| --- | ---: | ---: | ---: | --- |
| 平台 | 2,983 | 49 | 26,886,283 | `00a02f14b8a9c9d129e6173a6ec5b9cf6c8ed10e1eafedaf55000cd1d8a6ede3` |
| 独立 Full | 661 | 49 | 17,232,095 | `2f148ad34dd45d9dd7d6064b6258da288dcca682702b52d8fdc87ee8b910053f` |

两包各自的原前端输入分别为 502 份和 391 份，与 4ef3ca76 已核对的同类包逐字一致。两包均包含列明的十份前端许可或声明文件，包括原选择器的许可与来源声明。包内前端依赖保留 TinyMCE Vue 封装，未随包分发 TinyMCE 核心。三个 Tiny Cloud 示例配置的 key 都为空，真实编辑继续单独验收。

本轮核对构建复现。正式安装与浏览器业务分别见 [V778](empty-trusted-proxy-linux-v778.md) 和 [V779](patched-release-browser-v779.md)。
