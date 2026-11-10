# V939 当前发行 ZIP 的前端文件与默认配置

2026-11-10。使用 [V897 当前发行候选](current-linux-release-v897.md) 的平台和公开独立 Full Linux x64 ZIP。固定产品 68af5319148009f1a5484d2cbd1383b9e7b71ed2。逐项验证两包外层 SHA-256、发行清单与内含文件。

| 核对项 | 实测结果 |
| --- | --- |
| 根许可证和前端声明 | 两包均含相同的根许可证。每包包含 10 份前端许可与声明文件，含原选择器 MIT 许可、DCloud 基底许可及来源说明，也包含 TinyMCE Vue 封装、DOMPurify 和其他随包资源的许可文件。 |
| 原选择器 | 每包包含 7 份原选择器目录文件。两包许可与选择器内容逐份一致，比较时只规范化 UTF-8 BOM 和换行。 |
| 富文本依赖 | 两包锁定 TinyMCE Vue 封装 6.3.0 和 DOMPurify 3.4.17。TinyMCE 核心只在封装的可选 peer 声明中出现，未出现在实际锁定包条目或发行归档的核心文件路径中。 |
| 配置 | 平台源码、独立源码及独立宿主的三个 Tiny Cloud 示例配置均保留空 ApiKey、频道 8 和空付费插件列表。 |

本轮核对发行文件和配置。真实 Cloud 编辑、中文资源和图片上传仍待有效配置后验收。已通过的原空配置 Web 场景见 [V839](fixed-empty-tiny-cloud-web-v839.md)。实际选择器 Web 证据见 [V830](fixed-contract-options-web-v830.md)。
