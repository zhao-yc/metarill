# Java/C#固定Linux镜像准备

2026-10-31，V19。继续V17的真实同模型产物，补齐Server-200运行的容器构建上下文；不改变原页面、模板、接口或业务模型。镜像不是公开发布产品，正式双端应用、数据库和性能验收仍未通过。

## 已核对的固定输入

| 输入 | 本轮证据 |
| --- | --- |
| Java | V17 b6d64fb的原Spring Boot JAR，SHA256 `4452c60facdbdca8d24cf20c9657fe682281c1d73e2919aadcf1df196c838870`；未重编译、修改类或资源 |
| C# | V17 23db838的64个宿主Release运行输出，从原954文件冻结表按精确输出目录取出；DLL、完整deps/runtimeconfig、三份元数据、原生依赖及许可逐字保持 |
| Java基础镜像 | `docker.io/library/eclipse-temurin@sha256:db724162e4ff5f71fa0a2815026314d9e5e0e9a139f49be1d4717e6930e93341` |
| C#基础镜像 | `mcr.microsoft.com/dotnet/aspnet@sha256:43cbcf11fd6f88c0375c9c0f7fe59af619abeb323befe5f57dc1502b5a05d8c3` |
| 官方清单 | 从Docker官方library/eclipse-temurin的21-jre-noble及Microsoft aspnet的10.0-noble取得索引，唯一选出linux/amd64；重新取平台manifest并验证正文SHA256等于描述符digest，私有保存原索引/正文 |
| 上下文 | 离线工具真实生成，70个受固定清单覆盖的文件、约612MB，另有完成回执；两端各一Dockerfile，只COPY原构建产物，不restore、再生成或改Program |
| 回归 | 新上下文工具10项；性能工具全组66项通过、0失败/跳过；不累加重叠测试，不算Linux应用已运行 |
| 主机 | Server-200及192.168.31.200，每次Docker前验证，显式Unix socket；真实只读确认linux/x86_64、Docker磁盘约104GB可用、两目标镜像名不存在 |

官方清单digest是`FROM`基础镜像的内容身份；**不是**构建后应用镜像的Docker `.Id`，不得填入V18的`expectedImageId`冒充应用镜像证据。平台清单确认架构，不代替最终Linux版本输出；后续必须记录两个实际应用镜像ID和运行时版本。

## 可重复的离线构建入口

工具为`tools/performance/prepare_paired_images.py`，使用Python标准库，不调用Docker、网络、.NET或Java进程。私有配置声明版本/runId、两端40位源码提交、精确JAR路径/摘要、C#运行目录的**完整原冻结清单**及两个官方仓库digest引用。不能从当前可变输出临时计算一套新摘要冒充V17原产物。

```powershell
python tools/performance/prepare_paired_images.py --config .local/paired-linux-images-20261031/context-settings.json --output .local/paired-linux-images-20261031/new-contexts
```

输出必须是输入目录之外的新目录；旧目录和失败证据不覆盖。输入/副本/复制后的源文件均重新核对；缺失、增加、改字节、非规范路径、链接或Windows reparse、未允许配置、浮动镜像tag均拒绝。允许原`appsettings.example.json`，不接纳`appsettings.local.json`等私有配置。复制失败时保留intent，不写成功回执，不自动删除或重试。

Java额外核对Spring Boot启动器及原应用入口；C#核对net10.0、两个10.0共享框架、宿主/三份元数据及Linux x64 ELF原生库。文件头检查不代表原生库已在Linux加载。完整64输出保留Windows apphost及其它平台依赖以维持冻结清单，Linux实际ENTRYPOINT明确使用`dotnet /app/Application.Host.dll`。

两Dockerfile使用相同非root UID/GID10001、UTF-8与Asia/Shanghai时区。Java通过标准环境覆盖原固定127.0.0.1监听为容器内0.0.0.0:8080，保留原JAR；Redis仍为DB0，以本轮唯一spring.application.name提供原运行时前缀。C#用正式Production/Web与相同容器端口。没有打入数据库连接、密码、会话、Tiny Cloud key或生产配置；后续实际运行需另行准备受控私有配置及可写文件/导出/结构日志目录。

## 当前远端阶段与边界

本轮唯一runId `4c4ef93d9e4d432b8699e1cb10efc4cf`。固定归档已上传Server-200，远端71个归档文件摘要核验后，两个镜像构建及三个禁网只读探针均实际通过；探针退出后由独立Docker命令核验三个精确容器名均不存在。全程没有启动Spring/ASP.NET业务宿主、发送应用HTTP或连接数据库/Redis。基础层下载慢时沿用同一活跃构建至完成，没有另起或重跑本轮构建。

唯一准备目录为`/tmp/metarill-perf-images-4c4ef93d9e4d432b8699e1cb10efc4cf`，两目标镜像为`metarill-perf-4c4ef93d9e4d432b8699e1cb10efc4cf-java:artifact-v1`与`metarill-perf-4c4ef93d9e4d432b8699e1cb10efc4cf-csharp:artifact-v1`。构建的RUN只有创建本轮应用目录，不访问网络；固定基础层由Docker按digest取得。归档、归属和构建日志保留；没有删除共享基础镜像/缓存或其它任务对象。

三个精确名字的短时探针实际执行`java -version`、`dotnet --list-runtimes`和固定SkiaSharp原生库的ldd依赖检查，均退出0并独立核零。探针禁网、只读根、非root、丢弃能力、0.5CPU/256MiB/64PID；这些资源仅用于版本检查，不是后续同条件负载的资源证明。ldd无缺失依赖，不代表已经调用SkiaSharp图像API、生成缩略图或验证字体。

实际镜像与版本：

| 目标 | 构建后的应用镜像ID | 实测运行时 |
| --- | --- | --- |
| Java | `sha256:13634ed450e7c8e6165331f032fd5ebaee8e5ffe28d839d3d77fcf626329353f` | Temurin 21.0.13+9；V17原JAR仍为JDK21.0.12构建，区分构建/运行补丁版本 |
| C# | `sha256:4820313d7bf5f1684294251c496b46300102572eaba3c471c616d7f86455c81c` | Microsoft.NETCore.App和Microsoft.AspNetCore.App均10.0.14 |

两个镜像和唯一准备目录保留供后续使用；没有删除共享基础镜像或缓存。V18的镜像ID只能在正式应用容器确实使用上述镜像时采用，还须重新核对容器标签、实际回环绑定及资源。当前无应用容器，不能把三个版本探针当作正式接线或业务验收。

V08/MySQL首版仍缺两端显式结构/初始化、真实身份/权限及readiness、完整关系/父子/手写业务/文件等价、同数据/索引/SQL/资源/共享负载、正式性能和独占数据库/账号/样本清理协调器。当前镜像准备不建库、账号或授权，不复用旧随机租约，也不合main或生产部署。

私有证据根：`E:\git\metarill\.local\paired-linux-images-20261031`。保留runtime-image-proof、原manifest正文、context-settings、contexts/context-receipt、固定归档、image-build-scope、一次性执行器、Server-200只读预检及构建控制日志；不提交这些产物或私有路径配置。


## 固定Java宿主的登录缺口（后续静态核对）

对同一固定JAR重新核对SHA256后检查实际应用类：`SampleManagerService`、`SampleRoleService`、`SampleRolePermissionService`三份基础服务均在包内，`com.yinzon.controller.admin.sys.YzManagerControllerSys`不在包内。源文件也只装配这些基础服务和真实权限拦截器；既有WebMvcConfig只是排除了原登录路径，不能据此认为已提供登录实现。

原b6d64fb样板验收文档明确记录“网页登录态由测试准备，未覆盖真人密码登录”。该证据不能转记为密码登录通过。原平台登录Controller另有账号/IP失败锁、Cap验证码、密码校验、登录审计更新及Redis会话建立，不能仅取成功分支或预置Redis会话当作已经接入原认证。

后续要在新的可审查宿主基线补齐原登录所需接线，并实测正确/错误密码、禁用/撤权及相同账号角色；不得修改本批固定JAR后仍沿用旧摘要，也不能将readiness恒真、测试授权器或移除验证码作为省略接线的办法。本项是静态缺口，不冒充已执行HTTP404、应用启动或登录失败；`java-login-static-audit.json`保留原Controller来源摘要及包内类清单。当前镜像版本/依赖探针即使通过，也不消除此缺口。

V20已另建[原密码入口对照宿主](paired-java-login.md)，逐字接入原9方法及Cap/失败锁等依赖，独立构建17项/提取边界5项通过；原V19 JAR/镜像保持；新329f43e镜像及禁网版本/JAR内容探针已完成，Spring真实登录仍待，不将离线方法或内容探针转记为本批业务通过。

## V23 Windows CI编码修正

2026-11-01，新提交d8a09ec的CI run 41225573277在Windows性能工具步骤失败（66项中1 error），前端成功；Ubuntu当时仍运行。公开失败日志定位到test_prepare_paired_images.py三处read_text()未指定UTF-8，中文Dockerfile被cp1252解码失败。相同单项在本机-X utf8=0、默认cp936下复现gbk解码错误；此前本机UTF-8模式掩盖该测试差异。

仅将三处读取显式设为encoding=utf-8，不改生成上下文工具、Dockerfile、原二进制或镜像。修正后在同一Windows非UTF-8默认环境运行完整性能工具84项：82通过、2项POSIX专属跳过，无失败；POSIX资源18项另已在200真实Linux离线全过。完整GitHub CI须以新提交实际结果为准，不能因这次本机工具回归就宣称新CI或MySQL首版通过。旧23db838的run41206367405则已确认Windows/Ubuntu/前端全部success；该旧结果不覆盖后续工具提交。

## V46：固定旧应用继承与完整载荷核验（2026-11-03）

[两端采集镜像及原Linux Kestrel](paired-observed-images.md)已实际完成。官方基础镜像digest缓存不在但原应用还在时，上下文工具新增显式baseApplicationImages，仅接受原329f43e／ee05d99内容ID及其原官方运行时来源。Dockerfile用对应已验缓存标签，回执同时给出fromImages和baseApplicationImages；执行器必须构建前后核标签内容ID及实际父层，不把标签本身当不可变证明。普通官方digest模式不变，不自动探测或选择其它镜像。

首轮裸sha256:ID会被BuildKit解析成仓库名并失败，已保留原日志后修正缓存引用。本次为低磁盘余量使用21份C#变化文件及完整新JAR做增量层，但实际新镜像/app文件集合及全部66个SHA仍全量验证；不以增量成功代替完整载荷。原Program／六手写／393前端及Data摘要保持，五版本／内容探针和新原Kestrel匿名两请求经独立读取器通过，相关短容器新SSH核零。没有数据库、普通身份、配对业务SQL或负载成绩，原生采集配置及新精确实库范围继续待备。
