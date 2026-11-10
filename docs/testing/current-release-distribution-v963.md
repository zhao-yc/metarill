# V963 当前 Linux 双包安装文件与前端分发核对

2026-11-11。重新运行发行包校验器，对 [V943](text-projection-linux-release-v943.md) 的平台和独立 Full ZIP 核对外层摘要、提交 0b65780c、发行清单及全部载荷。两份归档摘要和 [V944 三拓扑实测](text-projection-runtime-topologies-v944.md) 的输入逐项一致。

| 核对项 | 结果 |
| --- | --- |
| 安装文件 | 两包均包含安装指南使用的宿主入口、依赖、运行时配置、元数据和示例设置。独立 Full 源码与宿主中的配置样例逐字一致。 |
| 前端许可与原选择器 | 根许可证一致。两包各含 10 份前端许可或声明及 7 份原选择器文件，规范化文本换行后逐份一致。 |
| 富文本分发 | 两包锁定 TinyMCE Vue 封装 6.3.0 与 DOMPurify 3.4.17。TinyMCE 核心作为可选依赖声明，锁文件与归档均未携带核心。三个 Tiny Cloud 样例的 key 和付费插件列表均为空。 |

安装指南已更新为当前双包及其三拓扑实测入口。当前独立 Full 的原合同选择器桌面与移动 Web 证据见 [V948](current-contract-web-package-v948.md)。本轮只重读发行文件和已有回执，真实 Tiny Cloud 编辑、生产域名和同条件性能仍按 [首版清单](../mysql-first-release-milestone.md) 分项验收。
