# V308 公开 Full 跨表覆盖导入实库复验

2026-11-06。V305 的正式覆盖验收使用私有检出中的夹具补丁。本轮将该业务检查接回仓库的正式测试入口，保留原追加模式及其既有租约。产品固定为 cc56aae，测试以 b420bfd 为基线。没有变更产品或前端。

追加与覆盖各自绑定固定包目录、设置变量、用途、产品来源和表数预算。包校验还会逐文件核对摘要、原模型及手写钩子。使用另一模式的固定包、用途或预算会被拒绝。真实入口使用待批准的无连接样本，确认在读取连接或启动宿主前拒绝执行。

| 验收 | 实际结果 |
| --- | --- |
| Release 严格构建 | 0 警告，0 错误 |
| 包准备和隔离门禁 | 22 通过，0 失败，0 跳过。追加与覆盖均从正规包内源码构建，分别核对 18 个匿名原入口、手写保护和文件漂移拒绝 |
| Server-200 正式验收 | 固定 2,600 份来源、156 份测试输出、1,279 份公开包文件。独占 MySQL 和 Redis DB0，44 表，1 项正式用例覆盖六个模型 |
| 实际业务 | 普通账号、原选档回调解析的 XLSX、原主键覆盖、稀疏存储、项目和墓碑排除、歧义拒绝、六次附件整批回滚、撤权、重启和注销通过 |
| 独立回收 | 新连接核对单库、临时账号和所有权记录不存在，Redis 容器与私有目录不存在，回环端口可用。合成存储及临时凭据已清除 |

离线入口为 `PublicR0CrossExcelNativePreparationTests` 和 `PublicR0CrossExcelNativeLeaseTests`。覆盖包可由 `METARILL_PUBLIC_R0_CROSS_OVERWRITE_PREPARE_OUTPUT` 输出到检出下的 `.local/public-r0-cross-overwrite-native/prepared`。正式入口为 `Production_public_Full_six_models_overwrite_original_excel_and_rollback_files`，使用 `METARILL_PUBLIC_R0_CROSS_OVERWRITE_NATIVE_SETTINGS` 指向同轮私有设置，且与 `METARILL_FRESH_INSTALL_SETTINGS` 指向同一文件。缺少本轮设置时正式用例明确跳过。

本轮原工作簿验证通过原选档回调与正式 HTTP 执行。浏览器实际选档、设备、完整链路并发和同条件性能分别验收。轮次 5e0415721796c043 的 TRX、脱敏结果和回收回执保留在本机忽略目录 `.local/cross-overwrite-tracked-v308`。
