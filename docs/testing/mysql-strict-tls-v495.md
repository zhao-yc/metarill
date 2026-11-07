# V495：MySQL 严格 TLS 与平台事务实连

日期：2026-11-07。起点为 `9714ee508c1c1c2cc1b7deb49943d0858860f13f`，最终测试与工具源码在本次提交。仅补验证、独占资源工具与 CI；平台运行时、原前端、生成模板和接口均未修改。小程序、Android、iOS 按用户再次确认，继续列在 MySQL 首版后的兼容性验收。

## 实际结果

最终轮次 `ddb590e06985af55` 在 Server-200 的独占 MySQL 8.4.11 容器运行。Windows 客户端实际加载 MySqlConnector 2.6.1；指定本轮 CA，服务端证书 SAN 精确包含 `deve-server.yinzon.com`，不包含 IP。容器使用固定镜像 `sha256:b3b90af2a6552ae30c266fdb7d5dd55f3afb72404bb78d37fe8a23eb857fd3fb`，1 CPU／1 GiB 内存，独占桥接网络、私有临时材料、内存数据目录及 LAN 28630 端口。

| 真实候选 | 结果 |
| --- | --- |
| VerifyFull，域名和指定 CA，TLS 1.3 | 成功，实际 TLS_AES_256_GCM_SHA384 |
| VerifyFull，同样配置强制 TLS 1.2 | 成功，实际 ECDHE-RSA-AES256-GCM-SHA384 |
| VerifyCA，指定 CA，以 IP 连接 | 成功，作为主机名错误候选的 CA 对照 |
| VerifyFull，以 IP 连接 | 明确证书验证拒绝 |
| VerifyFull，错误 CA／未导入系统信任链 | 两种候选分别证书验证拒绝 |
| REQUIRE X509，正确客户端证书和私钥 | 成功 |
| REQUIRE X509，不提供客户端证书 | 1045 拒绝 |
| REQUIRE X509，另一 CA 签发的客户端证书 | Schannel 明确 unknown_ca 状态拒绝，未把普通网络错误算作通过 |
| 正确严格 TLS 配置，错误密码 | 1045 拒绝 |
| 无账号级 SSL 条件的对照账号，严格 TLS | 成功 |
| 同一对照账号，禁用 TLS | 3159 拒绝，独占实例实际 require_secure_transport=ON |

这些是一个真实驱动测试中的 12 个向量，不额外计作 12 个测试。两个平台专项另验证：

- 数据库适配器和 FreeSql 均保留指定 CA／域名校验。四种错误目标分别在独立冷账号与已成功认证账号上验证，共 8 个拒绝向量。冷账号观察到驱动明确的“身份未验证，不发送密码”拒绝；缓存认证观察到证书异常。两者分别断言，不按泛化 1042 或含 certificate 的错误文本放行。
- `DatabaseSession.InTransactionAsync` 实际更新唯一测试行，主动抛出测试异常并回滚；新连接回读仍为 before。FreeSql 实际连接回读 TLS 1.3、非空密码套件和原行值。它证明现有平台连接／事务接线，没有新增启动 DDL。

冷认证的异常分类依据固定版本 [MySqlConnector 2.6.1 的 SendClearPasswordAsync](https://github.com/mysql-net/MySqlConnector/blob/2.6.1/src/MySqlConnector/Core/ServerSession.cs#L934)，证书告警采用 [Microsoft 的 Schannel 状态定义](https://learn.microsoft.com/en-us/windows/win32/secauthn/schannel-error-codes-for-tls-and-ssl-alerts)。这是对实际错误和驱动控制流的核对，未抓取线上密码流量，也不是恶意服务器或中间人攻击验证。

## 复现入口与验证

新增源码：

- `backend/tests/Metarill.Data.Tests/MySqlTlsIsolatedTests.cs`
- `tools/acceptance/server200_mysql_tls.py`
- `tools/acceptance/test_server200_mysql_tls.py`

资源工具只接受固定镜像、16 位随机 runId、强随机归属标识和 28000—28999 端口。按 prepare → 私有材料归档 → create → pin → 测试 → cleanup → 新 SSH status 顺序运行。每条 Docker 命令重新核对 Server-200 主机及 192.168.31.200，使用固定 Unix socket。容器和网络按真实标签验归属并用 ID 删除；目录必须为当前用户的 0700、固定文件集合、无链接且摘要不变。禁止用现有共享 MySQL 的证书、数据目录或 root 口令替代本轮材料。

真实 C# 测试需要 `METARILL_MYSQL_TLS_ISOLATED_SETTINGS` 指向私有 JSON，字段由测试 Settings 记录精确定义：固定运行号、域名／IP／端口、库／受限账号、临时口令、独占实例主机名／UUID、证书和证据绝对目录、六个客户端材料的 SHA256。缺配置时三个真实测试明确跳过；不从开发库配置自动推导。CA 不导入系统信任库，不设置“接受所有证书”回调。

```powershell
dotnet test backend/tests/Metarill.Data.Tests/Metarill.Data.Tests.csproj -c Release -warnaserror --filter FullyQualifiedName~MySqlTlsIsolatedTests
python -m unittest discover -s tools/acceptance -p test_server200_mysql_tls.py
```

| 验证 | 最终结果 |
| --- | --- |
| C# 测试工程严格构建 | 0 警告、0 错误 |
| 独占实例的 C# 专项 | 8 通过、0 失败、0 跳过；含 3 个真实测试与 5 个离线分类测试 |
| Server-200 Linux 资源工具边界 | 8 通过、0 跳过；不运行容器，仅验证拒绝及归属清理 |
| Windows 资源工具边界 | 2 通过、6 个 Linux 文件系统条件跳过 |
| Data 全组回归 | 1,593 通过、0 失败、32 个未配置实库的条件跳过；不把跳过算作通过 |

源码、实际测试程序集摘要、最终向量、TRX 计数、实例指纹及回收证明分别留存于 `.local/v495-mysql-tls`。CI 新增资源工具边界检查；该提交的远端 CI 按实际 SHA 另行跟踪，不以本机测试替代。

## 失败记录与资源回收

前四轮失败保留，不能算通过或合并测试数：

| 轮次 | 结果及原因 |
| --- | --- |
| 3239f94215b00ff0 | 0／2；内部网络不具备所需 LAN 端口转发，未进入 TLS。改为本轮独占桥接网络并增加真实 MySQL 握手前检。 |
| aa6bf13d92eda0f3 | 1／2；平台事务通过，客户端证书告警与账号级 SSL 拒绝的预期分类不完整。使用明确证书告警，另设无账号级 SSL 条件的对照账号验证全局安全传输。 |
| 66cf177c74d193e8 | 6／7；12 个驱动向量、事务通过，新增平台拒绝专项未识别冷认证的驱动身份保护。 |
| 21a4d96966d2118d | 6／7；仅增加脱敏错误分类，确认冷认证身份保护，尚未改变原通过标准。 |
| ddb590e06985af55 | 8／8；独立冷／热账号分别固定两条拒绝路径，真实连接、拒绝与回滚均通过。 |

五轮均按 ID 清理容器（带匿名卷清理参数）、网络及私有根；最终以新的严格 SSH 连接逐个复核容器、网络与私有根不存在。只有本轮容器内新建库、四个受限账号和一条样本，无共享库 DDL／DML。证书和临时配置先按用户要求归档并逐文件验证，再清理远端与本机临时副本；归档凭据保留，不进入 Git。

## 交付边界

本项关闭当前 Windows 客户端、Server-200 MySQL 容器的严格 TLS／双向证书／平台事务固定链。没有更改现有服务器证书、TLS 设置、应用私有配置或系统信任库。Linux 应用客户端、证书轮换／吊销／过期、恶意服务器／中间人和其他数据库未由此验收。Web 完整功能、未完成的关系／文件／工具组合、最终性能签收和首版总验收仍按进度表推进，不能将本项标成 MySQL 首版已经交付。

已有应用无需元数据、DDL、回填、再生成、运行时同步或 TLS 配置变更。本次回退只涉及新增测试、工具、CI 步骤和文档，不影响现有应用运行。
