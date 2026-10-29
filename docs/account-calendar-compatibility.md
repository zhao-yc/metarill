# 原账号日历时间兼容与只读时钟核验

日期：2026-10-29。基线 `3383925`；本批只修正共享认证运行时中账号日历窗口的解释。原页面、HTTP 字段、生成模板和数据库结构没有变化。C29 为本地通过、正式登录待验，不据此完成 MySQL 首版或性能验收。

## 问题与修复

原 Java 审计钩子调用 `LocalDateTime.now()`；原 Agent 账号有效期将 `LocalDateTime` 按 `ZoneId.systemDefault()` 转为瞬时。原 HTTP 日期仍是 `yyyy-MM-dd HH:mm:ss`，MySQL DATETIME 的无时区库值也不能直接当作 UTC。

修复前，真实 `AuthenticationService.AccountActive` 用 `DateTime.UtcNow` 直接比较日历值。在当前 UTC+8 宿主上，以当前日历前后十五分钟构造六个账号窗口：已到期的 validity_time/login_stop_time 被判有效，已经开始的 login_rise_time 被判尚未开始，实际错三项。该复现调用产品真实方法，不访问数据库、不登录或创建会话。

修复后三字段的无时区值按宿主 `TimeZoneInfo.Local` 解释，再与 UTC 瞬时比较。显式 UTC/Local DateTime 和 DateTimeOffset 保留自身瞬时；损坏值返回账号不可用，避免泄露解析异常。截止时刻必须大于当前瞬时、开始时刻小于等于当前瞬时，保留原 C# 边界包含规则；停用字段的已有映射不变。不写回或归一化原记录。

时区重叠选择较早瞬时，跳时向前跨越缺口，与已取证的 Java atZone 向量比较。宿主时区需要与原部署一致；本批没有新增用户时区、项目时区或改变 JSON 格式。系统会话 expires_at、OAuth、令牌和授权 TTL 继续使用 UTC，禁止全局替换 UtcNow。

## 实际证据与边界

- 在冻结原 Java 独立宿主的真实 classpath 上调用 `MyMetaObjectHandler.insertFill`：UTC 和 Asia/Shanghai 两次实际运行确认审计值采用宿主日历；约一秒测量间隔另计。不是复制 Java 时钟算法后自证。
- JDK 21.0.12 实际 `LocalDateTime.atZone` 生成14个向量并随测试提交；覆盖 UTC、上海、加德满都、纽约、柏林、Lord Howe 的偏移、跳时与重叠。日期两端只验证当前时刻的可用性，不声称 Windows/.NET 与 IANA/JDK 的全部历史时区规则逐刻相同。
- 修复后真实公共入口复验六个场景全部符合预期。Security 严格测试139通过、0失败、0跳过；覆盖三个字段的前后/相等边界、显式瞬时、损坏值及记录不变。
- 已授权 Server-200 上只执行28条时间字面量 SELECT，未创建库、表、账号、授权或写记录。每次 Docker 调用前核对服务器身份，MySQL8.4.11 实例 UUID 固定。管理员连接实际 session/global time_zone=SYSTEM、system_time_zone=CST、UTC偏移28800秒，SQL模式含严格模式，默认 InnoDB/utf8mb4/utf8mb4_0900_ai_ci。
- DATETIME/TIME 的 p=0..6、两组小数实际 CAST 均保留原结果；包含 `.123456` 到四位的 `.123500` 及零位进秒。这是表达式结果，不代替物理列写入/读取、应用驱动会话或完整 DDL 验收。服务器默认排序规则也不能证明已有数据库/列采用该规则。

私有证据目录 `.local/temporal-runtime-20261029` 保留只读脚本、native-readonly.json、实际 Java 输出与向量生成器、clock-before.log/clock-after.log、测试 TRX、摘要和生成回执；凭据、连接串与实际用户记录不纳入提交。没有本轮新对象，因而没有对象清理租约或删除动作。

## 本批最终回归

Agent351通过/25项缺实库配置跳过、API69/47、兼容18/11，均零失败；完整原样板的实际Full打包、更新、partial/原Vue保护及独立严格构建1项通过、零跳过。测试进程清除METARILL环境配置，不把跳过项算作业务验收。CLI严格构建零警告零错误，901份受核验源码在最终摘要窗口内未变。

215份既有生成/手写文件两轮逐字稳定，含24份手写partial；492份原前端逐字保持。首次复验误把输出的Generated命名空间后缀作为根命名空间，归属保护拒绝且未写文件；保留该失败日志后，按既有归属清单的rootNamespace重跑两轮通过，没有修改清单或降低保护。
## 尚未完成

真实应用 MySqlConnector/JDBC 连接会话、账号三字段实际读写与原页面编辑后的正式登录/逐请求重验仍待完成。本C29批次的 Service 审计仍使用当时 UtcNow；后续C30已修复生成源头，见[生成审计专项](generated-audit-calendar.md)。平台手写/文件库时钟及真实读写差异继续列为同条件基线缺口，不能据账号修复宣称全部审计时钟统一。A18 物理精度、V08/V14 的真实功能/数据/SQL/资源与性能门槛保持未完成。

## 跟进与回退

既有应用成套更新共享 Security 运行时并重新构建，保持与原部署相同的宿主时区。此批无需改元数据、DDL、回填、再生成页面或迁移 partial。若旧数据曾把 UTC 日历写入这些账号字段，需要先按来源逐项核对，不能自动整体加减八小时。回退只涉及本方法和构建产物，但会恢复已复现的日历窗口误判，应由维护者结合原值与部署时区决定。