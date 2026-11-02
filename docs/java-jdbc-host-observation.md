# 原Java宿主的请求／数据源采集接线（V43）

日期2026-11-02，起点35a01b8。只向独立Java验收宿主追加诊断源文件，不修改Metarill产品API、原页面、模板、元数据、业务权限或V37旧镜像；普通启动默认关闭。

## 真实接线与默认边界

PairedJdbcObservationConfiguration只有metarill.paired.sql-observation.enabled=true时才注册静态BeanPostProcessor及Servlet过滤器。未配置／false均不读取密钥或目录、不包装数据源；没有新端点、响应头或业务角色。过滤器排在原认证前以覆盖认证SQL，但原FilterChain和认证规则仍实际执行；不读取表单、cookie、连接串或业务响应正文。

PairedJdbcObservationHost包装每个实际DataSource Bean，两种getConnection重载均传给原池；不另建池、主动打开连接或改变原Bean销毁。启动核对实际／已包装Bean集合与明确data-source-names完全相等，额外、缺失或未包装源均不能通过。运行中发现新源也使证据失效。只覆盖此固定宿主的Bean数据源与实际注入路径；自行DriverManager建连、动态路由隐藏子源、反射绕过等仍须完整源码／运行覆盖审计，不能凭Bean名宣称全局覆盖。

每个请求线程显式begin／close scope，保留原JDBC连接、Spring事务、结果和异常链。原生JDBC经同一DataSource时加入相同采集；Spring事务内使用DataSourceUtils仍取得同一实际连接。异步请求、超预算、重复／无效采样标记、采集IO、未确认事务或source unwrap均使私有证据明确不完整，原链仍运行，不重试或改成新的HTTP错误。

## 私有配置与记录

只用于获授权的独占合成宿主，必须明确以下设置（前缀均为metarill.paired.sql-observation.）：

| 设置 | 含义 |
| --- | --- |
| enabled | 仅显式true启用 |
| run-id | 本轮32位小写十六进制标识 |
| data-source-names | 固定Bean名，逗号分隔，最多16个；原样板为dataSource |
| key-file | 仅本轮32字节私有值证明key文件，不把值放命令行／公开日志 |
| output-root | 既有普通私有目录；.metarill-paired-observation-owner内容必须逐字等于run-id |
| maximum-requests | 默认64，上限4096；超限仍运行原请求，但整轮证据无效 |
| maximum-commands | 默认4096，上限65536；沿用V42命令／事务预算 |

逐级拒绝链接／reparse路径，将普通Windows短名等规范为真实绝对路径；每个宿主实例创建全新随机子目录，重启不覆盖旧记录。POSIX子目录权限0700，Windows继承既有私有根ACL，不宣称额外修改Windows权限。采集不生成或更换输入key，关闭只清零内存副本。

协调器可发送X-Metarill-Sql-Sample（单一32位小写十六进制关联值）；它只关联私有记录，不授权业务、不作为文件名，也不作为开启采集的条件。缺省请求仍采集，重复值、多值或路径文本会使证据失效。文件名均由服务端UUID产生，不保存无效原文本。

每条记录为metarill-paired-jdbc-http-v1，包含采样关联、filter退出时状态、原链成功标志／异常类型及JDBC快照。statusAtFilterExit不是容器最后HTTP结果，响应R及最终状态仍须由真实客户端单独验证。Scope内部一致也不等于业务成功；明确错误用例可有完整失败证据。

index.json为metarill-paired-jdbc-index-v1，含固定源集合、请求开始／完成／已观察数、记录摘要、问题集合及finalized。协调器必须核对预期样本、实际trace摘要／内容、全部计数、问题为空及进程结束后的finalized；不能拿一个正常trace或running索引当作整轮完成。写入使用同目录临时文件和原子替换，失败不改原业务结果，也不能作为发布通过。SQL文本可能含原字面量，只能留在本轮私有目录。

## 兼容修正与实际验证

原宿主POM将Connector/J声明为runtime。首轮完整构建确认直接import驱动类型会编译失败；现适配器仅在采集时解析同一固定9.7.0公共API，读取getQueryBindings／BindValue，保留原POM和驱动范围，不执行SQL或猜测私有字段。真实驱动绑定、未知类型／流拒绝和原48项回归保持。

Spring错误码翻译专项先复现元数据连接回指为原未包装连接。现DatabaseMetaData只允许固定驱动已核源码为本地常量的getDatabaseProductName，并使getConnection回到同一包装；其它可能查询／逃逸的元数据API继续使证据无效。此修正不添加元数据SQL。

- 真实Spring Boot3.5.14／Spring6.2.18／JDK21严格编译及20项接线测试：0失败／跳过。覆盖未配置／false、事务内JdbcTemplate及原生JDBC同连接、原返回／异常／回滚、数据源生命周期、双源与重载、缺／额外源、错误码翻译、无脚本默认SQL初始化、异步／运输歧义、预算、所有权／key及IO失效。网络／数据库执行和Servlet请求仍是明确替身，不是原宿主真实Web／MySQL验收。
- 原独立宿主复制293个输入文件，包含原POM、业务源码和资源，逐项SHA保持；仅追加4个诊断源文件。最终原登录／验证码／类路径17项通过、0失败／跳过，完整可执行JAR构建成功。原JAR的361个类／资源条目逐字节保持，仅新增16个采集class，原MySQL9.7.0驱动SHA保持。原样板启动类、接口和业务权限未改。
- 性能工具129项：125通过、4个POSIX条件跳过。内部包含严格执行48项Java的1个Python测试，不重复相加。CI新增原runtime驱动范围下的20项Spring测试；新SHA的跨系统CI仍须实际查询。

私有证据：.local/java-jdbc-host-20261102/verified-summary.json；最终宿主冻结目录93f142ab8c494d27b9642d998430ab5e。保留首轮runtime编译失败、元数据红灯和中间构建，不累计为最终通过。新的JAR未部署，旧V37镜像／ended授权保持原样。

## 后续门槛

V44已补[C#每请求采集和私有索引](csharp-http-command-observation.md)及原包匿名装载子集；仍需冻结两端宿主及新精确范围，验证真实原Web／登录／业务SQL、绑定类型／同值证明、事务／回滚／重启及索引完整性。原Java服务器预处理、时间、批处理／保存点、所有自建连接路径及双端SQL语义未验；不缩减这些场景。没有启动容器、建库或账号，没有负载成绩，MySQL首版总验收继续未完成。

V45更新：[私有文件核验器](sql-artifact-audit.md)已实际读取20项Spring测试中保留的3命令／5边界产物，与独立固定预期一致；默认不导出测试目录，显式metarill.paired.test-evidence只复制本次合成JSON到全新位置、不导出key。Java值格式v2补纳秒输入证明，不截断原审计字段，服务器线上编码与原生HTTP／MySQL仍待。
