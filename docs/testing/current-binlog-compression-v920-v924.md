# V919–V924 同合同导入的 binlog 压缩对照

2026-11-10。目标是区分 binlog 体积与提交耗时的关系。Java、C# 使用与 [V915](current-child-isolated-control-v915.md) 一致的固定镜像、原工作簿、种子、索引、资源及默认排他父锁。每组使用单独的 MySQL 8.4.11，保留 ROW／FULL、`sync_binlog=1` 和 `innodb_flush_log_at_trx_commit=1`。只在独占容器启动时调整事务压缩。 [MySQL 事务压缩说明](https://dev.mysql.com/doc/refman/8.4/en/binary-log-transaction-compression.html)

V919 在脚本组装启动参数时失败，计时未启动，已创建的隔离资源核零。V920 修正后，关闭组完成 1,200 个正式请求、32 次预热及两端各 4,928 条物理明细的逐值核对。四并发三轮均触线，C#／Java 吞吐为 0.8850／0.8496／0.8206。新增统计查询的 12 个窗口均未取得有效聚合数据。关闭组资源独立核零，开启组未执行。

V922 在同一镜像的独占空实例确认查询返回 MySQL 1054。实际字节字段为 `COMPRESSED_BYTES_COUNTER` 与 `UNCOMPRESSED_BYTES_COUNTER`，和手册页面的列名不同。V923 使用实际字段再次独立验证，聚合计数从 0 笔增为 1 笔，压缩后 194 字节、压缩前 3,468 字节，两轮探针资源均独立核零。 [MySQL 压缩统计说明](https://dev.mysql.com/doc/refman/8.4/en/performance-schema-binary-log-transaction-compression-stats-table.html)

V924 已固定两组同镜像回放和实际统计查询。每组进入正式计时前，先从本轮独占实例确认模式、压缩级别及统计字段可读。两组正式结果待收口。本专项用于定位性能差异，发布门槛继续按 [V912 正式矩阵](current-exact-mysql-performance-v912.md)。
