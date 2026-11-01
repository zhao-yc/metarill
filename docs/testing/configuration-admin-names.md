# 原配置页后台名称与筛选兼容

日期：2026-11-02。编号：C96。起点：`fcf8f7187da51610b5e2ccf32dadb0d617a97936`；产品、测试、受管产物与进度在本任务同一提交交付。

## 问题和修复范围

C95真实原页面读取发现：配置页面记录已有后台ID，但根列表、选择列表及父表下子列表使用的VO缺少原Vue消费的`yzAdminTypeName`。配置表记录自身后台ID为空是另一种情况，不能填默认后台掩盖。

本次从生成源头复用已有固定角色后台名称证明，扩展到原配置表页及配置页面的三个既定用途。按固定表、页、来源列、目标字段及页面覆盖共同证明关系；唯一子页还须证明原配置表父键。任意List、SQL、别名冲突、错误父键或改动后的固定图不会被隐式放开。

名称属于读取投影，Entity和写入DTO不新增冗余名称字段。标准读取及写入后的只读回显补名，旧整实体请求中的名称回显进入剥离白名单，不能持久化。读取沿用真实来源动作、当前项目、软删除和父范围；不另加目标页面query权限。每批最多200个后台ID串行读取，最多5000个唯一ID；空来源及外项目、已删除目标的名称保持null。

同时恢复两个原界面已有筛选槽：配置表页的后台名称文本筛选，以及配置页面根列表的后台ID下拉筛选。前者生成当前项目活动后台的相关EXISTS，值交由ORM表达式处理；后者只接受单个int64数字或整数字符串，保留字符串大ID。空筛选跳过，错误类型和SQL文本拒绝。只有原搜索开关开启、来源可读且固定图证明成立才生成兼容分支，不扩大通用筛选白名单。

## 生成与回归

最终同一产品版本的相关回归为 **130通过、0失败、0跳过**，覆盖新配置页、既有角色页、读取IR、已发布投影、原整实体写入协议、固定页查询，以及当前完整管理包独立构建和匿名边界。该数不与早期38或92项相加，也不代表全后端或远端CI通过。

测试包含四页VO、无物理名称及可写DTO、旧请求回显剥离、隐藏来源、关闭搜索、错误固定图，以及真实生成程序集执行。离线执行使用真实FreeSql翻译，仅替换终端行传输并禁止数据库连接；证明当前项目、目标软删除、父范围、名称正向筛选、原ID筛选与来源动作拒绝。不能将其记作真实MySQL正向名称匹配。

固定SDK严格构建CLI，并通过正式CLI对平台与示例各生成两轮：215个文件逐字稳定，24份手写partial保持。原管理端`frontend/`及发布元数据未变；19项受管文件有意变化。八份生成Vue仅改变原只读／列表名称绑定，并在原选择事件保留ID赋值的同时补名称回显；其余模板、脚本及样式经精确差异还原后与基线一致。没有手补受管文件，没有新增依赖或路由。

核心命令如下；前端直接复用C95已固定并再次核对摘要的原HBuilderX产物，没有重写页面。

```powershell
.tools/dotnet/dotnet.exe test backend/tests/Metarill.Generation.Tests -c Release --nologo --no-restore --disable-build-servers --verbosity quiet -warnaserror --filter "FullyQualifiedName~FixedConfigurationAdminNameTests|FullyQualifiedName~FixedRoleAdminNameTests|FullyQualifiedName~ReadProjectionCompilationTests|FullyQualifiedName~PublishedReadProjectionCompilationTests|FullyQualifiedName~LegacyPageWriteContractTests|FullyQualifiedName~FixedPageQueryCompilationTests|FullyQualifiedName~Full_management_package_builds_from_its_own_sources_and_keeps_original_anonymous_boundaries"
.tools/dotnet/dotnet.exe backend/src/Metarill.Cli/bin/Release/net10.0/Metarill.Cli.dll generate --metadata backend/metadata/platform.json --output backend/generated/platform --namespace Metarill.Platform
.tools/dotnet/dotnet.exe backend/src/Metarill.Cli/bin/Release/net10.0/Metarill.Cli.dll generate --metadata backend/metadata/demo.json --output backend/generated/demo --namespace Metarill.Demo
.tools/dotnet/dotnet.exe publish backend/src/Metarill.Api -c Release --nologo --disable-build-servers -warnaserror
```

这里的publish只是本地构建，没有生产部署。

## 原浏览器与真实MySQL

使用原登录表单、既有设备／项目与原导航进入；最终正式Production宿主严格发布通过，浏览器使用原管理端产物，没有替换组件或调用Vue方法注入结果。

| 原入口 | 实际结果 | 边界 |
| --- | --- | --- |
| 配置页面根列表 | 首页14／总20，返回记录均含后台ID和非空名称；原后台下拉筛选成功 | 使用真实平台管理员，不代替普通账号矩阵 |
| 选择配置页面列表 | 首批10／总20，后台名称与原精确管理匹配字段同时显示 | 未修改原列、布局或操作 |
| 配置表详情下的配置页面 | 当前父表1／总1，后台名称正确；字段子列表15／总86仍可读 | 仅该既有父范围样本 |
| 配置表后台名称文本筛选 | 真实SQL执行、HTTP200／R200，现有后台ID为空，结果为空 | 未创建非空匹配样本；非空正向仅由上述离线生成执行证明 |

首阶段在原根页下拉选择后台后，实际得到HTTP200／R500“页面未开放该筛选字段”。保留该失败后，从源头补齐原ID筛选槽，完成最终130项回归、正规再生成与严格publish。仅重启本轮宿主到同地址，原会话及筛选条件保持，点击原“重试加载”成功恢复14／总20，名称继续正确。整轮26笔HTTP中保留这1笔首阶段错误，不能写成整轮首次全绿。

12张配置表共1614行的前后有序文本摘要一致；这不是全库备份或并发一致性证明。没有业务写入、DDL、初始化或新增账号／授权，正常登录和退出产生本轮自己的会话写入。退出后独立连接确认本轮会话活动0，正常审计墓碑保留。

独占浏览器空间已结束，只清理本轮唯一站点数据。归属核对后停止本轮宿主与代理，监听核零；私有配置副本和令牌单向摘要删除，原开发配置保留。776份保护基线内仅19项本任务受管变化，范围外0，不能写成776全部不变。待批B120冻结材料再次核验保持pending、未启动、无子进程和数据库写入。

## 失败保留及升级要求

早期夹具字符串／路径错误、错误读取元数据强类型属性的编译失败、离线连接配置和阴性断言校准均保留；最终产品不通过放宽错误图或授权断言获得通过。生成Vue比较首次错误地将新增名称赋值反向改为ID赋值，后仅校准比较规则并逐字核对；原浏览器一次失效元素引用中止后重新观察再继续，没有重播已成功操作。完整构建、TRX、截图、摘要、请求回执及收尾材料仅保留私有验收目录，不公开内部连接或记录。

适用范围是已证明的固定平台管理图。升级须成套更新生成源头，正规再生成Service、VO、Controller及配套生成页；独立管理包须重新打包、构建并验证。原管理端无需改页面、样式或交互；无DDL、回填和手写partial迁移。回退时使用相互匹配的生成器、受管产物及宿主，无需撤销业务数据。

本条只关闭固定四页名称显示、原两筛选槽和管理员读取子集。普通账号／撤权、保存回显真实写入、真实非空名称匹配、其它关系组合、并发、真机和性能仍待；不解除公开Full能力门槛、不消费B120新租约，也不关闭V01／V02／V10／V11总验收。

后续组合检查发现按钮更新回显误索取普通页面编辑权，C97已从源头保留独立按钮来源，见[按钮回显授权专项](admin-name-button-source.md)。该批离线修复不倒记为本页基线的真实浏览器写入或MySQL验收。
