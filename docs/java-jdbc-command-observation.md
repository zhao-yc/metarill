# Java JDBC命令观察基础（V42）

日期2026-11-02，起点98a8448。承接V39—V41的C#观察器，为固定原Java样板补充应用侧JDBC采集基础；本项V42尚未安装到正式Spring宿主，后续V43已补接线与完整原包（见下），未启动Java／MySQL或负载，不把离线驱动绑定对象当作真实服务器执行。

## 实现与接入边界

代码位于tools/performance：PairedJdbcObservation、MySqlJdbcObservationMetadata和独立SelfTest。没有组件扫描注解、Bean、HTTP开关、日志或默认启动行为；只有可信协调器明确begin并wrap原连接时启用。wrap不创建或打开连接、不改变原连接池或事务配置。后续须对固定宿主全部真实DataSource及原生连接路径统一接线，当前不能宣称全应用覆盖。

- 标准Statement／PreparedStatement的execute、executeQuery、executeUpdate、executeLargeUpdate均调用原驱动一次，返回值／异常对象和取消行为不被诊断异常替代。reader代理仅防止getStatement泄漏原命令，不读取、缓存或改变结果行；ExecuteQuery耗时只到API返回，不是全部结果读取时间。
- 固定MySQL Connector/J 9.7.0适配器读取实际getQueryBindings／BindValue的类型枚举、isSet、isStream及最终值；不调用getParameterMetaData、额外SQL或参数toString。服务器预处理标志按真实ServerPreparedStatement类型读取，不把客户端预处理当服务器准备。本轮只验证客户端绑定对象，服务器预处理仍需实库确认。
- JDBC事务及设置按实际setAutoCommit、commit、rollback、setReadOnly、setTransactionIsolation API记录（布尔值／明确隔离级别）；默认隔离仍须固定宿主预检证明，不额外查询服务器。setAutoCommit(false)是JDBC边界，不伪造服务器BEGIN；原提交／回滚失败保持。未见begin、保存点、批处理、手写事务控制SQL／事务内隐式提交SQL、未知类型／流／未绑定值、预算超限、unwrap／元数据逃逸、目标库／会话属性变更、跨线程或迟到访问均明确拒绝完整证明，原业务仍运行。
- 每个请求线程显式scope，禁止同线程活动scope嵌套；不把线程池任务自动归到同一请求。关闭时清零密钥副本并释放连接映射，计数与事务未确认结束会标问题。InternallyConsistent仅表示已包装边界内一致，不能证明没有遗漏DataSource、异步任务、绕过连接或完整业务成功。

只保存命令文本、绑定元数据／HMAC摘要、scope内引用令牌、原异常类型／SQL错误码、API边界和影响行数；不保存绑定原值、异常正文、reader行、连接串或驱动对象。实际SQL本身可能含字面量，未来真实trace只能在独占样本私有目录保存，不能直接公开。默认每类4096条、上限65536；SQL32768字符、256参数、字符串16384字符、字节值65536字节。任意精度数值和日历范围也有明确编码预算，未知对象不转字符串。

## 共同值证明与实际验证

共同文件sql-value-proof-v1.tsv由独立Python标准库HMAC生成，使用明确公开的合成测试key（32个字节11），不是部署秘密。Java和C#分别通过实际命令观察路径核对同一16组kind／摘要：null、空串、中文／引号／反斜线、高位整数、精确decimal及零、double及负零、single、字节、布尔、日期、无时区日历时间、时刻、扩展时长与GUID。格式沿用metarill-sql-values-v1；无效UTF-16、低于100ns的时间、未知时区对象／java.sql日期旧对象不静默归一化。

- JDK21.0.12 Windows严格javac -Xlint:all -Werror及48个SelfTest用例通过。原JDBC执行／事务是明确替身；驱动绑定专项使用真实9.7.0 NativeQueryBindings／NativeQueryBindValue，协议和字符集是明确本地替身，无connect、socket、握手或数据库查询。真实绑定的高位整数／精确decimal／微小double／null摘要与共同向量相等；stream／未绑定明确拒绝，不读取流。
- 性能工具完整Python回归129项：125通过、4个POSIX条件跳过、0失败。新Python测试1项在内部严格编译并运行48项Java，不能与129重复累计。缺JDK／固定驱动时明确跳过，不下载或冒充通过；CI增加JDK21及Maven固定驱动准备，跨系统作业仍须按新SHA实际查询。
- C#共同向量专项1项通过，包含于最终完整数据层1571项（1544通过、27实库条件跳过、0失败）；不将共同编码当作SQL／协议／ORM语义等价。

驱动JAR SHA256：0353648eaa1c91e0f4020c959abf756bc866ffd583df22ae6b6f6e0cbd43eb44。私有严格编译／执行日志、源码／输出摘要、完整工具日志和TRX位于.local/java-jdbc-observation-20261102。早期离线绑定夹具缺少未握手协议／字符集对象，另有匿名类字段重名编译和PowerShell JVM参数传法问题；保留为开发失败，仅最终冻结源码结果计入本项，不把中间37／40／44／46与最终48累计。

## 尚未完成

V43已补固定宿主DataSource／请求scope、私有留证与完整原包构建，见[原宿主接线](java-jdbc-host-observation.md)。仍须完整覆盖审计，再用新精确范围验证原登录／业务／Excel／文件事务和重启。尚未执行真实HTTP采集、Java服务器绑定／时间／批处理／保存点支持、双方SQL及事务语义对照，未重新构建或替换V37原三镜像。不得复用已ended授权。性能负载、原前端／跨端及MySQL首版总验收仍待，不以本项标完成。
