# 双端采集镜像与原 Linux Kestrel 核验（V46）

日期：2026-11-02—03；起点b1d0bcd。为后续同模型真实业务SQL采集准备两个新镜像，未替换V37旧基线、未运行新实库或性能负载。MySQL首版总验收继续未完成。

## 冻结输入及内容身份

| 项目 | 实际依据 |
| --- | --- |
| Java新JAR | V45严格49内部用例及原17离线测试对应产物；SHA256为130f295847cafa8fef3c1d82f817e52b7425dd5ea33ef8508beb2382f466e9e9；原293输入／361类资源保持，仅新增16个采集class |
| C#原完整应用 | 沿用V44实际装载过的8fe172f来源；当前b1d0bcd相对8fe的backend／frontend／六公开手写源差异为空，原Program／六手写／393前端文件逐项保持 |
| C#运行载荷 | 依原64个完整运行路径，加Metarill.Paired.SqlObservation.dll／pdb共66文件；从V44冻结输出逐字复制，不拿本次新编译的不同二进制冒充旧摘要；七份独立类库deps旁文件不属于宿主运行发布集合 |
| 实际Data | SHA256为89d2a4bbf48d550a985a1c16a627e2db3659209566559f7fce1f868548eb1e55，采集组件源码与V44最终版本保持 |
| Java父应用 | 原329f43e4d3678bf2d81cfd6d7f73baa3ad73676134798dc7018d3ab28fb85936，原登录宿主及JRE来源保持 |
| C#父应用 | 原ee05d99dbb52de8a51f7d42fcbb7a67d1b588c8bb0b637f937a8cba236975200，V32已验64文件的完整应用 |

普通官方digest上下文模式仍保持。新增可选baseApplicationImages只允许上述固定双应用及其原官方运行时来源；不能由调用方传任意内容ID、标签或自动回退。Dockerfile的FROM使用两父应用原有缓存标签，回执同时记录实际FROM引用与批准的内容ID。离线上下文本身不查询Docker，执行器必须另行核实际标签在构建前后仍指向指定ID、父层继承及新镜像完整文件。

## 实际环境与有界构建

200只读预检确认原两应用仍在，但原官方基础镜像digest引用已不在缓存；当时可用空间仅1719738368字节。未清理共享缓存、历史材料或数据库。原完整上下文仅在本机冻结，实际传输为21份变化C#文件及完整新Java JAR，含构建文件共106499959字节；其余运行文件通过精确父应用层继承，不能省略最终全量检查。

首轮直接写FROM sha256:内容ID，被BuildKit解析成docker.io/library/sha256仓库，远端元数据解析403后终止，未生成新镜像、未运行探针。原失败日志及归属保留。第二版先确认首轮终态与两目标／五探针不存在，再验证全部已上传文件；只替换两Dockerfile和清单，其余通过普通文件硬链接复用，避免再次占用约百MiB。

每条Docker均先核Server-200／192.168.31.200，显式Unix socket；构建network=none、pull=false。上传前及解包／每次构建检查动态空间并保留至少1GiB余量；不凭旧磁盘观察继续写。第二版最终两个新镜像如下：

| 目标 | 新应用内容ID | 实测运行时 |
| --- | --- | --- |
| Java | sha256:99face186258dd73aa4343477ba94506f73954bf8a961be137fca2622df9d2a0 | Temurin21.0.13+9 |
| C# | sha256:5da49219cfa9c47c49977333d45738dbadb4dd0d09f7d6bce04c6ff55ec73ed5 | Microsoft.NETCore.App／Microsoft.AspNetCore.App10.0.14 |

本轮构建runId为4f9300cc7b2148a7b29bd47f681ab284；新标签为metarill-perf-此runId-java／csharp:artifact-v1，唯一目录/tmp/metarill-perf-images-此runId。两新镜像实际RootFS层以前述父镜像为前缀，构建前后父标签内容ID未变。原镜像未删除或改写。

五个短探针均禁网、不发布端口、只读根、非root10001、丢弃全部能力、0.5CPU／256MiB／64PID：Java版本、.NET共享框架、Skia ldd依赖、Java完整JAR摘要、C#全部/app文件集合及逐项SHA。五项通过后，新SSH独立确认五容器不存在、原驱动PID／startTicks已结束、两镜像及父标签身份保持。剩余空间1518465024字节；版本／文件证明本身不等于原应用业务运行。

## 新 C# 镜像实际原宿主采集

另一个独占短容器使用同一5da4921镜像，网络none、无端口、非root／只读根、0.5CPU／512MiB／128PID。独立BCL探针先再次核/app恰为66文件及全部摘要，再以dotnet启动镜像中的原Application.Host.dll；没有改Program、Controller、认证实现或元数据。

原宿主为Production／Web，连接配置明确为空、开发初始化关闭。仅显式加载原采集组件，使用32字节11的公开合成key，不生成或传输真实口令。仅TCP等待监听，不发送未标记health。以下两条原接口各请求一次，带独立预先固定的sample：

- GET /api/admin/auto/sample_contract/81002/detail/1：HTTP401／R401。
- GET /api/admin/auto/sample_customer/81001/detail/1：HTTP401／R401。

SIGTERM让原Kestrel／DI正常关闭，子进程退出0；没有新增HTTP关闭端点。取得index及两份请求记录后，以独立固定预期／SHA通过真实CLI严格核验：finalized、请求2／2、两个scope关闭、命令及事务均0、Data摘要精确匹配；即便强制cp1252管道，V47修正后也成功。新SSH再确认本轮HTTP容器不存在。

这关闭V44之后“原Linux Kestrel实际加载采集组件”的匿名子集。Java原Spring的真实HTTP／MySQL采集、C#普通身份及真实SQL、配对事务／时间／完整连接路径、性能负载仍待；不能将匿名零SQL当全业务覆盖、物理数据库或SQL语义等价。performanceAccepted／releaseAccepted保持false。

## 保留证据与下一步

忽略目录.local/paired-observed-images-20261102/4f9300cc7b2148a7b29bd47f681ab284保存完整／增量清单、原包保护、两次构建及首因、第二版日志／父层／五探针、新SSH核验，以及csharp-http-probe下实际原Kestrel源／严格构建、原始记录、独立预期／CLI报告和清理回执。远端只保留本轮新镜像、无密来源及日志／中间目录；没有清理共享或旧验收材料。

本次另外保留两次SDK发布摘要不匹配：首次自动嵌入当前Git提交号；仅固定SourceRevisionId仍会受SourceLink当前提交影响。未放宽V44原Data摘要，最终沿用已验证冻结二进制。旧V37等实库授权已ended；新镜像尚未接入原生协调器的新采集配置、普通业务／重启和新精确实库范围，不能直接套用旧租约或宣布MySQL首版完成。

最终完整工具153项：149通过、4个POSIX条件跳过；包含13项镜像上下文（新增3项）及21项文件读取专项，内部Java49项含于1个Python用例，均不重复相加。新提交跨系统CI仍须实际跟踪，不能把本机／五探针／匿名两请求替代MySQL首版验收。


## 2026-11-03：V48原业务采集链

[新执行链](paired-observed-native.md)已接入V46镜像，保留原六初始化和真实三普通身份，每端9身份＋37原CRUD／业务／关系请求，正常关闭后保存私有SQL供独立审阅。Windows194过4POSIX跳过，Linux197过1缺JDK跳过，实际实库业务／SQL次数与语义／性能仍待；新用途不能复用已结束V37授权，也不会自动把实测计数记为验收预期。
