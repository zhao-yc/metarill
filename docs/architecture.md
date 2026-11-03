# 架构与实施边界

当前明确交付节点（2026-11-02用户确认）：**先完成并验收MySQL首版**。其它数据库适配保留后续路线图，不作为本节点完成条件；原界面、功能／权限／事务／生成、跨端与同条件性能要求保持，详见[MySQL首版交付范围](mysql-first-release-milestone.md)。

保留 YZP 的项目、后台类型、表、字段、页面、页面字段、按钮、主子关系和代码生成模型。

普通请求：Controller → Service → Mapper；复杂流程：Controller → Business → Service → Mapper。

平台和生成应用采用 .NET 10 / ASP.NET Core。FreeSql 承接普通持久化；元数据、权限、结构快照、结构变更计划和生成归属由 Metarill 管理。禁止启动时自动建表改表。

菜单采用单表树，页面直接归属后台类型。ID 对外为字符串；更新区分未提交与 null。保留 /api、R<T>、分页及现有接口语义。

分页`PageResult.Total`／`Pages`及生成Count的`R<long>.Data`保持原JSON数字语义，不套用标识的字符串转换；其余long标识继续输出字符串。计数不缩小为int32或浮点，Agent／MCP schema同步反映整数。手写纯标识响应应使用字符串或DTO。已有工程通过既有受管更新流程同步Core及对应宿主的计数JSON登记并重新编译；本项无需元数据、DDL或数据回填，原接口、页面和Service类型保持。详见[V52／V53](paired-observed-v52-counter-contract.md)。

长期适配 MySQL、MariaDB、PostgreSQL、SQL Server、Oracle、SQLite，首期只验收 MySQL。平台库和业务库分别选择，一个业务项目绑定一种数据库，不实现跨库分布式事务。

前端继续使用 uni-app / HBuilderX；其他协议或交互改动需先确认。开发数据库容器只在核实身份后的 200 服务器运行。

独立应用包含 Core / Data / Exports / Files 基础工程。文件结构来自平台模型的确定性子集；业务模型与基础设施仅在显式结构同步时组合，基础表通过专用文件接口访问。文件观察器与生成 CRUD 共用实际 DatabaseSession 和事务，普通宿主启动不执行 DDL。平台面向其他业务库的文件接线仍需按项目选择同库服务，不能用平台文件库伪装跨库原子引用。


生成/手写代码通过同程序集partial分离：Entity、页级DTO/VO、Mapper、Service、Controller及导入请求可直接访问同类成员。DTO/VO、Mapper、Controller继续sealed，主构造函数仅在生成部分声明；Service手写壳只首次创建，其余扩展另建非归属`.Extension.cs`。前端和JSON合同不因partial自动改变，Controller辅助方法须NonAction、计算属性按需JsonIgnore/Column(IsIgnore=true)。已有工程更新生成器后再生成并编译，无SQL或元数据回填；具体规则随两种独立宿主README下发。

## 文件对象供应商

文件行、权限与引用仍使用各项目的同一数据库会话/事务，对象层默认本地；S3服务器中转已接固定私有配置、不可重解释的定位、实际字节校验和原可寻址只读流。没有添加表或前端入口，不自动创建桶；平台与独立宿主共用实现。普通/管理包与真实旧应用升级离线闭环已有证据，真实存储/供应商、跨库权限、原浏览器、直传/OSS及性能仍待，不能以SDK或编译宣布支持全部云端。见[S3范围与配置](s3-object-storage.md)。


数据库Web登录失败在认证回滚后以独立事务持久化原`yz_manager_log`事件，25字段结构由security逻辑元数据与统一适配器管理。记录只取可信失败原因及身份／IP，不写口令、令牌或异常正文；宿主拥有外层锁时由外层边界负责单次落库。旧工程先同步受管运行时／元数据并显式预览、执行及核验结构，禁止启动自动改表。实现及未验范围见[V57](login-failure-audit-compatibility.md)。

成功登录明确写last_login_time／login_ip／update_time，与会话插入同事务，故障整笔回滚；用户名只按原Java规则去除两端ASCII空白，密码不归一化。日志基础模型由固定派生工具可重复形成，平台使用既有受控物理图，完整管理包保留编译内旧资产用于来源核验，当前新资产仍不增加页面权限。详细边界见[成功审计](login-success-audit-compatibility.md)及[失败审计](login-failure-audit-compatibility.md)。

原账号／IP失败锁由共享Redis DB0存储承接，平台与完整宿主固定项目／部署库隔离；缺配置明确拒绝，锁检查早于Cap与密码查询，认证回滚后计数、成功外层提交后清计数。普通请求限流独立存在。业务数据库适配层不承担Redis命令，无新增表／启动DDL；跨应用重启与Redis自身持久化分别说明。配置、原规则与未验范围见[登录失败锁](login-failure-lock-compatibility.md)。
