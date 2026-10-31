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

本轮唯一runId `4c4ef93d9e4d432b8699e1cb10efc4cf`。固定归档已上传Server-200，远端71个归档文件摘要核验后，已提交Java镜像构建。当前尚未取得两个成功镜像及三个探针的完成回执；实际构建进度以私有执行日志核对，不能把Dockerfile或构建中状态写为运行通过。

唯一准备目录为`/tmp/metarill-perf-images-4c4ef93d9e4d432b8699e1cb10efc4cf`，两目标镜像为`metarill-perf-4c4ef93d9e4d432b8699e1cb10efc4cf-java:artifact-v1`与`metarill-perf-4c4ef93d9e4d432b8699e1cb10efc4cf-csharp:artifact-v1`。构建的RUN只有创建本轮应用目录，不访问网络；固定基础层由Docker按digest取得。归档、归属和构建日志保留；没有删除共享基础镜像/缓存或其它任务对象。

准备完成后预定三个精确名字的短时探针，仅执行`java -version`、`dotnet --list-runtimes`和固定SkiaSharp原生库的ldd依赖检查。探针禁网、只读根、非root、丢弃能力、0.5CPU/256MiB/64PID，退出自动删除并另行核验不存在；这些是待核实步骤，不是已通过。不会启动Spring/ASP.NET业务宿主、HTTP、数据库、Redis或实际文件操作。

V08/MySQL首版仍缺两端显式结构/初始化、真实身份/权限及readiness、完整关系/父子/手写业务/文件等价、同数据/索引/SQL/资源/共享负载、正式性能和独占数据库/账号/样本清理协调器。当前镜像准备不建库、账号或授权，不复用旧随机租约，也不合main或生产部署。

私有证据根：`E:\git\metarill\.local\paired-linux-images-20261031`。保留runtime-image-proof、原manifest正文、context-settings、contexts/context-receipt、固定归档、image-build-scope、一次性执行器、Server-200只读预检及构建控制日志；不提交这些产物或私有路径配置。
