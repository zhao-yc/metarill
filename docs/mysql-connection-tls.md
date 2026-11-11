# MySQL 严格 TLS 配置

Metarill 使用 MySqlConnector。`SslMode=VerifyFull` 同时校验证书签发链和连接主机名。`Server` 填写数据库证书覆盖的 DNS 名称。使用私有 CA 时，通过 `SSL CA` 指定宿主可读取的 CA 文件绝对路径。 [MySqlConnector 连接参数](https://mysqlconnector.net/connection-options/)

```text
Server=mysql.example.org;Port=3306;Database=app_db;User ID=app_user;Password=替换为实际值;SslMode=VerifyFull;SSL CA=/etc/metarill/mysql-ca.pem
```

把连接串保存在私有配置中，`ExpectedDatabase` 与 `Database` 一致。平台分别配置 `Metarill:Database`、`Metarill:BusinessDatabase`，以及需要使用的 `Metarill:ProjectDatabases:<项目ID>`。独立应用在 `appsettings.local.json` 中配置顶层 `Database`。每个配置独立提供该目标库的连接身份和证书信任材料。

数据库管理员配置覆盖服务 DNS 名称的证书，并按部署策略要求安全传输。MySQL 的 `require_secure_transport=ON` 会要求 TCP 连接使用加密传输。Metarill 普通启动只提供宿主就绪信息，首次安装应核对连接目标后执行显式初始化，再用真实账号和一个业务页完成登录与读写核验。 [MySQL 8.4 加密连接](https://dev.mysql.com/doc/refman/8.4/en/using-encrypted-connections.html)

现有实例迁移到严格校验时，先以该环境的真实域名和 CA 进行连接验证，再更新私有配置并重启。自动生成的 MySQL 证书可能不匹配部署域名。更换 CA 后按相同步骤复验平台、业务库和独立应用。

当前发行候选 abbe89fb 已在隔离 MySQL 8.4.11 核对证书链、主机名、三库显式安装和两种宿主的原业务接口。部署时使用目标数据库的真实域名及 CA 复验。[V1080 当前候选严格 MySQL TLS](testing/mysql-strict-tls-web-v565.md#v1080-当前发行候选严格-mysql-tls)。
