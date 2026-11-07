# Java/C#同模型独立构建与固定

2026-10-31，V17。此批将性能实验所需的两侧离线产物更新并固定，不是运行时、数据库或性能验收。原3表、46物理列、6索引、3页面、3按钮及1主子关系保持；没有删除全局来源、关系、文件或手写业务换取打包成功。

## 固定来源及本轮实际结果

| 项目 | 已执行证据 | 边界 |
| --- | --- | --- |
| Java源码 | 独立干净b6d64fb2ad4ac25b004eeaa326092de3cfb7f311，原正式生成应用347文件逐字复制到新目录；原生成双轮证据仍属10月28日 | 本轮重新构建，未重新运行Java生成测试 |
| Java独立构建 | JDK21.0.12/Maven3.6.3，离线重新package及SampleClasspathTest；1通过/0失败错误跳过，JAR103135232字节 | Spring未启动、未登录、不访问MySQL或Redis |
| C#源码/CLI | 已推送23db83877a46736e04c57f9ffae47a1d936f6c80独立归档2235源码，正规CLI严格构建零警告错误 | 原工作区程序集不作为此次来源 |
| C#完整包 | 同版CLI的FullApplication，初始661文件/包含原前端；安装六份公开手写源，通过原HostCustomization partial注册，不改受管Program；完整宿主严格构建零警告错误 | 不替入开发身份、控制器或ORM模拟实现 |
| 重复生成 | 同一正规CLI两轮，34个生成目录文件字节稳定、全部六份手写源保持 | 本轮不重新生成平台/示例自身 |
| 物理共同结构 | 固定Data的真实MySqlSchemaPlanner对转换输入及实际包内元数据分别离线编译，3表/46列/6索引；对原Java DDL差异0，含9个datetime(0)及6个原审计默认 | 仅离线编译，未检查实际数据库或执行DDL |
| 完整元数据 | 以真实MetadataCatalog解析两份JSON并序列化全部配置后JsonNode.DeepEquals为true：表/列/页面/按钮/主子均保持 | 原始文件字节SHA不同，不能写成两个JSON文件逐字相同 |
| 构建后固定 | Java716文件、C#954文件逐项SHA256，原源码347/2235均保持；原三动作类确实进入JAR，C#六份手写源逐项核对 | 包内bin/obj是本次构建产物，独立列出，不冒充Git源码 |

Java JAR SHA256：`4452c60facdbdca8d24cf20c9657fe682281c1d73e2919aadcf1df196c838870`；C#正式宿主程序集：`c74d62e6d2795491ed44add3c0ee0ffd717601400b167dd676dd5986d8230f66`。这些摘要固定本次产物，不宣称不同时间/目录构建可产生完全相同JAR字节。

C#转换输入元数据SHA为`f7e342a17da8bd34d0f50adc2d55094e194294364bacde8b1d970ebdc1e471dd`，包内项目JSON为`12bc9e1ffa6cecccd78d4adca78a671064ed9f01607f90a46d10160fabe71ec2`。两份文件分别保留；完整类型语义相等且物理计划相同，不以物理列数量代替页面/按钮配置相同。

## 手写业务与运行时前提

原Java预览/处理/回滚三动作已有对应C# Controller、Business、按钮适配、摘要及Service partial。V17从公开样板六份源安装，没有重写实体/Mapper/CRUD；实际正式动作仍按C19待验，不能延用旧说明中的“尚未移植”作为当前代码状态，也不能把本次编译当正式业务通过。

Java样板宿主仍明确说明：权限工作台激活受原默认readiness拒绝，导航和授权接入须另验。本批只确认该源码/说明边界，未判断正确初始化后的普通业务登录结果；后续必须以原真实身份/权限实测，不能将readiness恒真或测试授权器作为实验前提。C#为原web-database-v1完整包，六份手写业务经公开partial钩子注册；实际Web/OAuth、文件及两端角色等价仍待。

Server-200只读检查已先核对hostname=Server-200和192.168.31.200，再显式调用远端unix Docker socket查询镜像。所查`mcr.microsoft.com/dotnet/aspnet`、`mcr.microsoft.com/dotnet/sdk`、`eclipse-temurin`三个仓库未发现缓存；检查范围仅这三个仓库。没有拉镜像、构建镜像、创建容器、库/账号/Redis键或任何负载；不使用本机Docker。

## 距离正式性能实验仍缺

1. 准备可审查的两个独占容器/业务库、受限账号及Java Redis命名空间执行与清理协调器，固定Linux运行时镜像、CPU/内存/交换/CFS/CPU集合；新的外部对象需明确范围，旧租约不复用。
2. 显式安装/初始化、正常真实登录和同角色权限、原三动作/关系/父子/文件/失败回滚等价。Java权限激活或其它前提若不成立，先修复实际宿主接线并保留证据。
3. 同样样本规模/分布/ID、全部索引、数据库时区/舍入和实际SQL/参数类型/次数/事务范围。两份离线DDL等价只证明计划，不能代替这一项。
4. 原JSON及固定原附件下载场景、三轮交替预热/正式负载和资源采样、按既定阈值审查稳定退化、核验并清理本轮独占对象。V16二进制工具可复用，原XLSX/ZIP的内容等价与吞吐另按实际合同处理。

V08/V11/MySQL首版总验收保持未通过；本批不推进后续数据库，也不合main或部署生产。

## 复核入口与证据

私有根：`E:\git\metarill\.local\paired-runtime-20261031`。`preparation-intent.json`、两个源码/构建后逐文件清单、`paired-build-receipt.json`、`regeneration-receipt.json`、`metadata-semantic-proof.json`、`csharp-package-physical-plan.json`和`server-runtime-images.json`分别保留构建、生成、语义、方言与只读环境证据。所有配置仍为模板/环境占位，没有生成正式凭据。

C#实际使用固定CLI执行`package --metadata ... --output ... --namespace Metarill.GlobalSample --source-root ...`；六份源来自`tools/acceptance/sample-host`，实际build为Release/-warnaserror/--disable-build-servers；随后同一CLI对backend/generated执行两次generate。结构及完整语义审计是私有只读.NET控制台，分别直接调用实际MySqlSchemaPlanner和MetadataCatalog，不创建DatabaseSession。Java直接用已缓存Maven classworlds启动器执行`-B -o -f pom.xml -DskipTests package`及`-Dtest=SampleClasspathTest test`；没有升级工具链或依赖。

原46列/15差异及B98历史包/失败保持原身份；本批更新的是新的双端产物。平台原前端/模板/元数据与生成776项保护核对不变；本批无需产品DDL、回填或旧项目迁移。


V19后续已补[固定Linux镜像准备](paired-linux-images.md)：由本批原JAR/64个运行输出生成上下文，实取官方linux/amd64 manifest，200架构/磁盘及唯一目标名核对后实际构建双镜像，Java21.0.13/.NET10.0.14/Skia依赖三个禁网只读探针通过并独立核零；镜像保留但未启动业务宿主，原应用、数据库及性能门槛继续保持。

V19后续对同一JAR的静态审计另确认：三份基础账号/角色Service已装配，但原网页登录Controller不在包内；旧样板说明的Web会话来自测试准备。需另补原认证接线和真正登录，不能以镜像版本探针、预置Redis会话或放宽readiness替代。详情及未执行的HTTP边界见[Linux准备登录缺口](paired-linux-images.md)。

## 2026-11-02 当前候选更新（V32）

原V31修订版2固定旧e210c31／49649d4且从未正式执行，现已保留快照并superseded；实际旧入口口令／外部调用／启动标记均0。V32从当前63f60dc重新正规生成同一完整模型，6手写与两轮34文件稳定、原完整元数据及物理3表46列6索引等价；新ee05d99 Linux镜像完整64载荷及原21／9禁网HTTP通过，两容器新SSH核零，资源工具只更新固定C#ID及旧ID拒绝反例。Windows116过4POSIX跳过／Linux120全过均为工具子集。新正式双库仍需当前工具／材料固定与精确直接授权；有效身份、业务／SQL及实际负载尚未执行，V08／首版仍未通过。详见[当前产物与边界](paired-current-runtime.md)。
