# 双端实际SQL观测前提只读核对（V38）

日期2026-11-02，起点7dfa1fd。V37只关闭原双端初始化／普通身份子集；本项核对下一步真实SQL／参数／次数／事务的采集前提，不执行负载、不创建或删除对象。

## Server-200实际状态

经严格SSH、每条Docker前核主机／IP、显式Unix socket及固定MySQL实例pin，只读取版本、资源变量和performance_schema消费者／仪器配置：MySQL8.4.11，performance_schema=false，目标消费者／仪器查询为空，max_connections=200，innodb_buffer_pool_size=1073741824。本次没有读取statement SQL_TEXT、其它账号或线程，没有开启日志、授予权限、修改SESSION／GLOBAL配置或重启服务。

不能把消费者空表解释成应用未执行SQL，也不能据此生成SQL次数／耗时证据。共享MySQL是否有并行负载及正式测量资源条件仍未固定，1GiB缓冲及200连接上限不是无竞争证明。本次不尝试改变共享实例观测配置。

## 当前代码边界与下一步

DatabaseSession使用FreeSql强类型CRUD；MySqlDatabaseProvider另可创建原生DbConnection，SqlRowStore和MySqlNonRedundantReader包含直接DbCommand路径。只挂FreeSql AOP会遗漏授权／文件／逻辑联接等直接命令，不能宣称完整SQL覆盖。

当前基础设施已有MySqlFloatingCommand执行包装：FloatingMySqlAdo命令工厂、SqlRowStore和MySqlNonRedundantReader使用它，连接／事务继续交原驱动。这是可评估的应用侧观察入口，不代表现在已具备SQL采集；还须逐项确认未包装命令、事务开始／提交／回滚、准备后真实参数类型、取消与失败、跨异步及请求隔离，并证明观察不开连接、不改命令／参数和原浮点Prepare逻辑。

下一步先准备仅限独占合成应用的采集合同与覆盖验证：两端都要记录实际SQL、参数类型／同值证明、次数和事务边界；登录凭据／令牌／连接串不能写公开报告，未知或漏采须明确拒绝。不以生成SQL文本或响应散列代替真实执行。语句采集与正式性能负载分开；任何新实库／容器／账号或观测配置变更仍按明确新范围执行，不复用已ended的V37。

## 证据与限制

私有只读程序、stdout／stderr及result.json位于.local/paired-sql-observation-preflight-20261102；外部只执行配置查询，没有应用连接或业务请求。本项无产品／原页面／模板／元数据变化，尚未实现统一观察器、证明实际SQL等价或得到性能成绩。V08与MySQL首版继续未完成。
