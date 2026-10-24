# 架构与实施边界

保留 YZP 的项目、后台类型、表、字段、页面、页面字段、按钮、主子关系和代码生成模型。

普通请求：Controller → Service → Mapper；复杂流程：Controller → Business → Service → Mapper。

平台和生成应用采用 .NET 10 / ASP.NET Core。FreeSql 承接普通持久化；元数据、权限、结构快照、结构变更计划和生成归属由 Metarill 管理。禁止启动时自动建表改表。

菜单采用单表树，页面直接归属后台类型。ID 对外为字符串；更新区分未提交与 null。保留 /api、R<T>、分页及现有接口语义。

长期适配 MySQL、MariaDB、PostgreSQL、SQL Server、Oracle、SQLite，首期只验收 MySQL。平台库和业务库分别选择，一个业务项目绑定一种数据库，不实现跨库分布式事务。

前端继续使用 uni-app / HBuilderX；其他协议或交互改动需先确认。开发数据库容器只在核实身份后的 200 服务器运行。

独立应用包含 Core / Data / Exports / Files 基础工程。文件结构来自平台模型的确定性子集；业务模型与基础设施仅在显式结构同步时组合，基础表通过专用文件接口访问。文件观察器与生成 CRUD 共用实际 DatabaseSession 和事务，普通宿主启动不执行 DDL。平台面向其他业务库的文件接线仍需按项目选择同库服务，不能用平台文件库伪装跨库原子引用。


生成/手写代码通过同程序集partial分离：Entity、页级DTO/VO、Mapper、Service、Controller及导入请求可直接访问同类成员。DTO/VO、Mapper、Controller继续sealed，主构造函数仅在生成部分声明；Service手写壳只首次创建，其余扩展另建非归属`.Extension.cs`。前端和JSON合同不因partial自动改变，Controller辅助方法须NonAction、计算属性按需JsonIgnore/Column(IsIgnore=true)。已有工程更新生成器后再生成并编译，无SQL或元数据回填；具体规则随两种独立宿主README下发。
