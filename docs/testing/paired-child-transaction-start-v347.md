# V347 同父导入事务启动实库专项

2026-11-06，产品基线 `e9a1d45`，Server-200 独占库轮次 `2d81a731307e979c`。MySqlConnector 固定为 2.6.1。

合并缓存写回入口调用 `BeginTransactionAsync(RepeatableRead)` 时，驱动会设置会话隔离级别再启动事务。本次省去此前额外发送的单事务隔离命令。启用合并仍要求连接重置、独占创建的 MySQL 事务和原路径的其他条件。嵌套事务继续沿用外层事务及原回滚规则。 [MySqlConnector 2.6.1 事务启动实现](https://github.com/mysql-net/MySqlConnector/blob/2.6.1/src/MySqlConnector/MySqlConnection.cs#L131-L168)

实库专项直接调用修改后的 `DatabaseSession.InScalarCacheImportTransactionAsync`。前次借用把会话和下一笔事务分别设为读已提交，返回连接后由产品复用同一物理连接。实际隔离为可重复读。同一事务的普通查询仍读到旧快照，当前锁读读到独立连接提交的新值。缺失键的共享锁读阻止独立连接插入，原事务提交后该插入成功。驱动观察记录到一次开始和一次提交，应用层额外隔离命令为零。使用独立连接核验本轮库、账号和所有权记录均已清除。

严格数据层回归 1,573 项通过、0 项失败、29 项因环境条件跳过。实库专项严格构建零警告、零错误。本次只验证拿父锁前省一次驱动往返及其事务语义，正式完整应用性能判定继续沿用 [V342](paired-child-scalar-cache-timing-v342.md)。父锁等待、持有及提交区间仍需在同负载下分别核对。
