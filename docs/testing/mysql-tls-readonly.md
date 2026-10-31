# MySQL 驱动只读 TLS 模式验证

日期：2026-10-29；进度编号 C39。产品起始源码 94d3628；本批只增加测试和文档，不改驱动、应用配置、原接口或前端。完整 MySQL 首版与 TLS 部署仍未结项。

## 固定范围与实际结果

已通过严格主机密钥校验的 SSH 核实 Server-200 主机/IP与 MySQL 实例，随后只读取当前公开 CA、服务端公钥证书和 TLS 变量。每条 Docker 前均执行主机/IP核对，没有取得私钥、建库、建用户、DDL、DML或更改服务器配置。真实驱动从既有私有配置读取受限业务账号，不复制连接串；每个候选为新连接，成功连接进入只读事务，仅查询实例指纹及本连接加密状态，最后回滚。

当前 MySQL 8.4.11 的自动生成证书 CN 不是业务域名，没有 SAN，公开 CA 不在本机系统信任链。与之对应的五个实际结果如下；“拒绝”是预期验证结果，并非已登录通过。

| 客户端候选 | 实际结果 | 证据范围 |
| --- | --- | --- |
| Required，无显式 CA | 连接成功 | 实例/库指纹匹配，TLSv1.3、TLS_AES_256_GCM_SHA384 |
| VerifyCA，无显式 CA | 证书验证拒绝 | MySqlException → AuthenticationException，未开放连接 |
| VerifyFull，无显式 CA | 证书验证拒绝 | 同上；不能据此单独区分信任链与主机名原因 |
| VerifyCA，指定已固定公开 CA | 连接成功 | 实例/库指纹匹配，TLSv1.3、TLS_AES_256_GCM_SHA384 |
| VerifyFull，指定已固定公开 CA | 证书验证拒绝 | CA 对照能成功，而严格主机名模式失败；结合无匹配 CN/SAN，支持域名不匹配这一解释 |

实际加载 MySqlConnector 的 InformationalVersion 为 2.6.1，不能以 AssemblyVersion 2.0.0.0 误判驱动版本。本轮没有证书回调、信任所有证书、系统信任库导入、全局 TLS/SQL mode 修改、客户端证书替换或连接配置写回。服务器当前 require_secure_transport=OFF，只是只读观察，不是平台修改，也不代表服务器已强制加密。

## 测试与复现

源码是 backend/tests/Metarill.Data.Tests/MySqlTlsReadOnlyTests.cs。九项离线配置门禁拒绝错误主机/库、root账号、IP、实例UUID、CA摘要及相对输入/输出路径，先于DNS和数据库连接；另一个真实用例顺序执行上述五种候选，只有预期状态和证书错误分类同时成立才通过。网络失败或密码失败不能代替证书拒绝。

真实用例需要显式环境变量 METARILL_MYSQL_TLS_READONLY_SETTINGS；缺配置时跳过，不从其他实库租约推导许可，不创建或清理数据库对象。该私有 JSON 只含这些字段：

- ConfigurationPath：既有私有开发配置的绝对路径，读取 Metarill.BusinessDatabase 的 ConnectionString、ExpectedDatabase；不得提交该原文件。
- CaPath、CaSha256：公开 CA 的绝对路径及文件 SHA256；前后重复核验，不导入系统信任库。
- OutputPath：新的独占绝对输出文件，已存在则拒绝覆盖。
- ExpectedConnectionHost、ExpectedDatabase、ExpectedIpAddress、ExpectedHostname、ExpectedServerUuid：显式固定目标；DNS结果、登录后实例和数据库均核对。
- SystemTrustExpected、HostnameMatchExpected：按当前证书/系统实际检查填写；此轮均为 false，不能当成所有部署的默认规则。

配置只有路径引用和非秘密预期，不复制口令。运行命令为 dotnet test backend/tests/Metarill.Data.Tests/Metarill.Data.Tests.csproj -c Release --filter FullyQualifiedName~MySqlTlsReadOnlyTests；指向当前 .NET SDK，并显式设置上述环境变量。运行完移除该进程环境变量。输出只保留模式、结果、协议/密码套件和异常类型，不保留驱动原文、栈、连接对象、用户名、口令或令牌。

本轮10项（九门禁加一个真实五模式用例）通过，零失败、零跳过；最终Data测试工程严格构建零警告/错误，Data整组1151通过、23实库条件跳过；移入专属配置后同一严格程序集的TLS10项零跳过通过。整组23跳过包含本次TLS，没有把缺条件项算成通过；原生用例中的五种候选也不额外相加为五个测试。首轮编译的一处可空警告已修复，原日志保留；不能把带警告首轮写成严格构建通过。只读结果、固定来源/输出摘要及脱敏TRX保留在 .local/mysql-tls-readonly-20261029，不提交证书、配置或日志。两份公开证书已按项目名备份到用户指定的 codex重要文件备份，并附中文说明及摘要；无私钥。

产品源码、模板、已生成文件及原前端共1158个受管文件与94d3628的Git blob核对，内容无变化；137个文本只有工作副本CRLF/LF差异，按文本换行单独记录，二进制仍严格逐字比较。不把跨克隆原始字节摘要不同误判成产品改动，也不宣称这些文本跨克隆逐字相同；C38自己的固定来源仍保持原始字节核验且pending/not-started。

## 尚未覆盖

本轮只证明当前 Server-200 和当前客户端上的这五种组合。没有匹配真实域名证书的 VerifyFull 正向部署、双向 TLS、显式禁用/不支持 TLS 的服务器、TLS1.2专项、Linux/容器证书链实连、轮换/吊销/过期证书、恶意服务端或中间人测试；也不证明密码在所有失败路径中的发送先后次序，官方公告修复与项目专属攻击验证须分开陈述。

应用原连接设置保持，未向平台发布或替换服务器证书；无需元数据/DDL/回填、再生成或运行时同步。撤回本批只需移除测试和文档，不影响任何线上库或应用。C38独占单库/账号租约仍待直接授权，本轮只读实连不消费或扩展它。
