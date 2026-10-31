# 对照Java宿主的原密码登录接线

2026-11-01，V20。本项为同条件Java/C#性能基线补齐原密码登录代码，不改变Metarill业务/前端，不修改V17固定JAR或V19镜像，也不把离线组件测试计为真实登录通过。

## 来源和实际产物

原Java基线b6d64fb的样板只有账号/角色Service、权限拦截器及由测试准备的Web会话，未提供原网页登录Controller。新建独立宿主，逐字保留旧347份来源，再从同一固定Git对象补入原登录方法及依赖；不预置Redis会话、不注入认证替身或把readiness设为恒真。

- `tools/performance/LoginHostSourceGenerator.java`使用JDK语法树提取原Controller的9个完整方法、19个字段，保留cap-config、form登录、账号/IP失败锁、Cap验证、密码散列、成功审计及Redis会话建立，方法正文逐字不改。
- `tools/performance/java-login-sources.tsv`固定14项Git原始字节SHA256，包括原Cap三个类、MD5/IP工具、登录失败日志实体/Mapper/Service/实现、事件枚举及原V8日志DDL/验证码测试。日志基础CRUD直接复用原来源，不手写新的实体、Mapper或CRUD实现。
- 原RestTemplate配置已在347文件中，Git LF与原样板CRLF仅换行不同；对原Git摘要及既有样板摘要分别校验，保留其原字节，不用通用归一化覆盖冻结来源。失败输入不会进入写入阶段，已有不同文件/重复回执拒绝。
- 新控制器为`com.yinzon.samplehost.PairedBaselineLoginController`，保持原`/admin/sys/yz_manager/login`及`cap-config`和R/data/map协议；其它角色、导航、Agent、文件及业务CRUD仍沿用原样板来源，不复制整个平台Controller或钉钉业务。

| 实际结果 | 证据与边界 |
| --- | --- |
| 原来源保护 | 347文件逐字保持，原V17 JAR SHA4452c60仍保持，未替换旧产物 |
| 新宿主固定 | 362份来源、749份构建后文件分别逐项固定；提取记录包含原9方法/19字段摘要 |
| 独立构建 | 同原JDK21.0.12/Maven3.6.3，离线package成功，无平台JAR/classes依赖；新JAR 103159885字节 |
| 新JAR摘要 | `e97ca4f25476712097eabcdb88cf4ac6a5d6c718c43adc729d3ad134b6c48b58`；不是原V17 JAR，不能继续沿用旧镜像ID |
| 原Cap回归 | 原来源7项，启用后的缺token、缺配置、无效token、远端异常均拒绝，正常验证和公开endpoint正确 |
| 新原登录MVC回归 | 9项，真实form绑定/原MD5/字符串ID/TTL/响应密码盐清空、错误密码无会话并计数审计、账号5次/IP20次锁、启用Cap拒绝及正常通过、secret不下发 |
| 类路径 | 原1项独立类路径回归；合计17项，0失败/错误/跳过 |
| 提取边界 | 实际JDK5项正反例：正常提取、重复回执、源字节变化、已有Controller冲突及越界输入；失败前后文件清单/字节保持 |
| 包内核验 | 新登录/Cap/日志类及路由常量已进入JAR；日志DDL只在test resources，未进入运行JAR或自动执行 |

上面的9项MVC与7项Cap回归使用Mockito隔离账号、Redis和Cap传输，调用实际提取的Controller及原验证码实现；没有建立Spring实际业务宿主、真实MySQL/Redis登录或请求外部Cap。不能转记为真实账号权限/登录通过，更不能代替两个后台的同条件性能或浏览器验收。

## 复核命令

先按原347来源清单建立新宿主，并由固定b6d64fb Git对象按TSV提取原来源根，不能在旧冻结目录直接改包。JDK源码模式命令：

```powershell
java tools/performance/LoginHostSourceGenerator.java tools/performance/java-login-sources.tsv <固定原来源根> <新宿主根>
# 将公开PairedBaselineLoginControllerTests.java复制到新宿主同包的src/test/java位置。
# 使用新宿主原Maven第三方依赖；本批实际采用缓存Maven classworlds启动器和-o离线模式。
mvn -B -o -f <新宿主根>/backend/pom.xml -Dtest=SampleClasspathTest,AdminCapCaptchaVerifierTests,PairedBaselineLoginControllerTests package
```

提取器只写源文件，不编译或启动应用。原源码来自版本化逐文件清单，不需要在Metarill生产构建中引入Java。实际本批原始构建使用`C:\Users\zhao-\.jdks\ms-21.0.12\bin\java.exe`和缓存Maven3.6.3启动器；日志及17项Surefire XML分别保存。首次参数拼写/既有HTTP配置换行预检失败均在批量写入前，保留原日志；未减项或绕过Cap。

## 尚未完成的实际接入

新Jar已固定到下述独占Linux镜像，但尚未启动真实Spring业务宿主。后续仍须显式准备独占业务/基础库及Redis命名空间、原日志表DDL、真实密码与同角色权限、普通/禁用/撤权、导航及工作台readiness、文件/三业务动作后再进入性能回放；没有重写旧V19标签冒充旧产物。当前没有建库/账号、Redis键、Cap私有配置或部署；原登录Properties默认及启用后的拒绝规则均保持，不因缺少真实配置关闭验证。

私有证据：`E:\git\metarill\.local\paired-java-login-20261101`，包含原Git来源、14项清单、提取前后记录、5个边界用例、源/构建后逐项摘要、login-host-preparation-receipt、原始Maven日志及Surefire报告。V08/MySQL首版总验收保持未通过；新Jar不混入待批C19固定包或其单库租约。


## Server-200新镜像与内容探针（2026-11-01）

提取工具/清单/测试源的原始字节均与提交`4e8d0cb386de7d69b322596061886faedf748b6b`匹配；原Java上游仍为b6d64fb，两者分别记录，不能把提取工具提交说成原JAR全部源码提交。沿用V19已固定的Java21基础manifest，在Server-200构建唯一镜像`metarill-perf-f85f7ad246414026a3f257ee936f0a5c-java:login-v1`，实际ID`sha256:329f43e4d3678bf2d81cfd6d7f73baa3ad73676134798dc7018d3ab28fb85936`。镜像标签记录本轮工具提交，JAR内容摘要另行固定。

禁网、只读根、非root、丢弃能力、0.5CPU/256MiB/64PID的单个短时容器实际执行`java -version`及`sha256sum /app/application.jar`，JDK21.0.13+9及镜像内新JAR摘要e97ca4f均符合固定输入，退出0；随后新Docker命令独立确认该探针容器不存在。上下文3文件复制前后摘要保持，每次Docker前都验证Server-200主机名/192.168.31.200并指定Unix socket。

新镜像及唯一`/tmp/metarill-java-login-image-f85f7ad246414026a3f257ee936f0a5c`准备目录保留，原V19镜像未被本轮命令改动。没有启动Spring、发送应用HTTP、连接MySQL/Redis或创建库/账号/键，版本和内容探针不算真实登录、Bean完整接线、图像/文件业务或性能通过。私有image-intent、image-build-scope、image-result及native-image-proof保留构建和探针证据。
