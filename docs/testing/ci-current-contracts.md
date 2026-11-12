# CI现行合同与原页面补验

日期：2026-10-30。产品起点为 `8a263509cdb549a960b40cb0014077cc3df1c2eb`，本轮只修改六份测试及本说明、ID展示说明和进度表，不修改产品代码、元数据、生成模板、前端或CI工作流。没有连接数据库、执行S3对象操作、消费既有正式租约或部署。

## 失败来源

实际读取 `c2b0e24d65a2a568fe26f35b15b9b0beb41a6e37` 的[Linux CI原始日志](https://github.com/zhao-yc/metarill/actions/runs/41011456326/job/123918110043)：生成项目2229通过、6失败、257条件跳过。该运行前端成功；读取时Windows仍运行，不能写成整体成功。在当前产品上独立复现同六项：0通过、6失败、0跳过，原TRX和日志保留。

| 用例 | 原失败原因 | 当前检查与保留边界 |
| --- | --- | --- |
| TrustedAdminProjectOptionsTests 的隐藏项目UI检查 | 把能力清单与页面一起逐字比较，且仍要求可信模块报告已接线的Runtime A缺口 | 原页面全部逐字比较，原路由逐项比较；单独解析能力清单，只允许固定角色协调缺口消失，schema和supportedControlSubsets及其它缺口保持。普通目录仍有缺口并拒绝Full。隐藏项目不新增CRUD页、DTO写入口或客户端可信标记 |
| TrustedSecurityServicePartialsTests 的首次创建 | 仍要求可信三服务返回Runtime A告警 | 真实必需partial、RequirePrepared、摘要检查和再生成手写字节保护保留；可信接线不再虚报缺口，普通模型仍拒绝。相邻真实负向编译及空方法拒绝一并运行 |
| PlatformConfigurationIdPresentationTests 的ID展示 | 旧整文件脚本摘要覆盖了后来用户批准的List远程分页脚本变化 | 不拿当前页面重算新摘要。用改型前真实六字段事实作为独立对照，两份当前模板落盘详情完整字节相等；未受分页影响的child页继续使用原整文件SHA。原DOM/样式及主前端十二页冻结基线另行保持 |
| TrustedSecurityFrontendAssetsTests 的实际资源准备 | 仍要求已接可信运行时留下Runtime A缺口 | 四项适配需求保留，可信缺口清单为空；普通目录仍缺口并拒绝。原资源、原路由、准备只读/防御复制、真实Node闭包以及伪造/篡改来源拒绝继续检查 |
| NonRedundantCrossTableGenerationTests 的路径导入门禁 | 沿用“尚未确认非空路径”的旧措辞 | 用户已确认无损还原规则，生成链已接，但正式往返/事务验收仍未完成。明确核对201页/place_id/detail的精确缺口、可编译写计划、Full实际抛错且不建输出；不开放该用途 |
| DerivedRelationNamesTests 的隐藏派生关系 | 对实体未带完整命名空间的源码拼写作旧子串匹配 | 真实构建后反射检查私有方法及Task<ISelect<目标实体>>返回类型，拒绝公开Options入口，来源条件仍保留；不以拼写检查代替类型和可见性 |

六字段独立事实取自A15前提交 `bd800bfa969775d462dada3135950118432063be` 的 `backend/metadata/platform.json`：1679391146044956674、1670726646479638529、1670726646546747393 原为string/nvarchar(50)，2200000000000000135、2200000000000000137、2200000000000000139 原为string/varchar(50)。前三项有“字符”展示事实，后三项未声明该属性。测试显式记录这六项，不调用待测展示转换来构造期望值。其它当前元数据保持相同，借此隔离“存储改型是否改变UI”与“另行批准的模板变化”。

## 扩大回归与Windows行尾

先修六项的定向运行6通过、0失败、0跳过。随后七个完整相关类首轮116通过、2失败、0跳过：两个完整应用理论用例把原 `remoteRelationOptions.js` 的CRLF字节与打包器既有LF输出直接相比。实际来源有61处CRLF；现有FrontendPackager文本合同本来就统一LF。

仅规范这两个用例的来源行尾，再与包内实际UTF8字节完整比较，不修改组件、打包器或期望内容。原包自身restore/build、能力清单、真实更新器和更新后手写partial逐字保护继续执行。保留首轮失败TRX，不覆盖为通过。

最终严格编译并运行下列七个完整类：**118通过、0失败、0跳过**（3分18秒）：

- TrustedAdminProjectOptionsTests
- TrustedSecurityServicePartialsTests
- TrustedSecurityFrontendAssetsTests
- PlatformConfigurationIdPresentationTests
- NonRedundantCrossTableGenerationTests
- DerivedRelationNamesTests
- FixedRoleAdminNameTests

原前端全量Node测试另行运行：**320通过、0失败、0跳过**，包含十二原页/十六字段的冻结UI、同步保护、远程候选及表单联动合同。DOM与样式金样未改。六项定向结果与118完整类结果重叠，不相加为124项独立验证。

私有证据保留于 `.local/ci-current-contracts-20261030`：`baseline-ci-failures.json`、`six-current-red.log`、`six-current-fixed.log`、`adjacent-classes.log`、`adjacent-classes-final.log`、`frontend-final.log` 及各自TRX。实际命令为本机SDK `dotnet test ... -warnaserror --filter <上述完整类>`，前端为 `npm test --prefix frontend/metarill_uni_admin`。进程均已正常退出，无本轮常驻服务。

## 尚未完成

本提交最终SHA的Linux/Windows全量CI需独立核对；本地118项不代表生成项目全部用例或所有后端项目完成。旧失败运行不改写为通过。

原List接线的真实MySQL、普通身份/完整父范围、其它打开方式、多值控件、原生设备及同条件性能仍在V02/V10及相关行。可信运行时缺口消失只说明已实现固定接线，不证明C05完整正式矩阵或首版验收结束；跨表路径导入Full门禁继续关闭。C57真实S3和云存储矩阵亦不因本轮测试通过而完成。


## 2026-11-03：a86c43d 双平台八失败

GitHub run41502116815已终态；Windows／Ubuntu均在Generation同8项失败，前端成功。本机固定起点复现8失败／20通过／0跳过；原失败TRX与日志保留，不改写旧结果。

- NonRedundantCrossTargets的pathImport：当前独立派生目标已支持普通路径导入，改为实际Full正例；负例使用仍未证明的覆盖导入。
- GlobalSingleFreshCatalog的targetR0：全局R0目标已接线，改为真实Full及原Vue探针正例；坏名称覆盖、项目归属、凭据投影等8种反例保持。
- GlobalSharedCachedTarget的四种R0键组合及原长R0控件：已验R0要求无缺口并创建真实Full；未验R1继续要求缺口与不可发布准备标记，手写保护、原组件字节、原Excel开关和实际编译保持。
- NonRedundantCrossTableExcel的普通夹具：来源父范围不属于已验独立R0用途，补齐后台归属仍须拒绝；保留BackendOnly严格构建，不为消除红灯放宽产品审计器。

本轮未修改GenerationCapabilityAudit／NonRedundantCrossTableSubset或其它发布门禁。上述测试修正与V53的数字计数产品修复分开论证；新SHA的跨系统全量CI另验，相关类通过也不转记为MySQL首版通过。当前结果见[V52／V53](../paired-observed-v52-counter-contract.md)。

## 2026-11-03 固定程序集的 Vue 依赖与复验

本机使用固定 `6f6bfe2` 的生成测试程序集，首次六类回归 98 通过、10 失败、0 跳过。失败均在子进程调用原 Vue 探针，分布为三种完整跨表包、五个全局单控件和两个共享级联组合。探针使用编译时源码位置对应的 `package.json` 解析依赖。冻结目录没有安装 Vue，单独从该位置运行 Node 模块解析复现 `MODULE_NOT_FOUND`。

核对冻结目录与工作目录的 `package-lock.json` 逐字相同后，仅对复验进程设置 `NODE_PATH` 为现有锁定依赖目录，确认 Vue 3.5.44 和 SFC 编译器可加载。全局单控件完整类 13 项通过，跨表三个真实 Full 包的构建、更新、手写保护和原 Vue 探针 3 项通过，均零失败零跳过。已按相同六类筛选再次启动完整回归，尚未记录通过。上述三份报告及摘要保存于私有 `.local/current-generation-locked-20261103`。

固定程序集、原业务断言和前端模板均未改动。局部复验与原批次有重叠，不相加为完整 CI 或正式 MySQL 验收。

整组最终结果为 **108 项通过，0 失败，0 跳过**。复核两个 TRX，108 个用例名称逐项一致，原 10 个失败均在本次实际重跑通过。报告 `generation-six-locked.trx` 及 SHA-256 已加入同一私有证据目录。本次为六类本地回归，当前提交完整 GitHub CI、正式 MySQL、设备和性能仍单独验收。

## 2026-11-03 f3c36f4 双轮生成及依赖布局复核

按冻结提交 `f3c36f4e375c59dc46ce2484037c1e07e2ba33bb` 创建独立检出，CLI 以 Release 和警告视为错误构建。用生成 CLI 连续两轮重建平台与示例，再执行原前端的模板版本、写入契约、自定义路由和 List 候选四项同步。215 个生成目录文件和 499 个原前端文件均按 SHA-256 与检出基线逐文件比对，两轮无差异或额外生成文件。原工作区未参与生成写入。

首次在无本地依赖的检出运行同步，`sync-original-list-options.mjs` 无法解析 `@vue/compiler-sfc`。核对两份锁文件相同，并将锁定依赖放入独立检出的 `node_modules` 后，实际 ES 模块导入和上述两轮同步通过。两个原工作簿用例在依赖就位前失败，用同一固定生成程序集重新运行后 2 通过、0 失败、0 跳过。该复验只覆盖原工作簿用例和生成稳定性，完整后端与 GitHub CI 结果单列。没有执行实库、设备或性能负载。

## 2026-11-03 f3c36f4 九个后端测试项目

将固定 `f3c36f4e375c59dc46ce2484037c1e07e2ba33bb` 检出到独立目录，保持测试程序集位于各项目默认的 bin 路径。使用锁定本机依赖、Release 严格构建，关闭外部服务配置。以下项目各自完整执行，计数来自九份不同项目的原始 TRX。

| 项目 | 通过 | 失败 | 跳过 |
| --- | ---: | ---: | ---: |
| Metarill.Agent.Tests | 447 | 0 | 25 |
| Metarill.Api.Tests | 224 | 0 | 49 |
| Metarill.Compatibility.Tests | 18 | 0 | 11 |
| Metarill.Data.Tests | 1552 | 0 | 27 |
| Metarill.Exports.Tests | 35 | 0 | 0 |
| Metarill.Files.Tests | 256 | 0 | 59 |
| Metarill.Mcp.Tests | 30 | 0 | 0 |
| Metarill.Metadata.Tests | 576 | 0 | 25 |
| Metarill.Security.Tests | 174 | 0 | 1 |

首轮使用仓库之外的输出目录，兼容性、数据及元数据部分测试无法沿程序集目录找到元数据。上述结果来自修正布局后的完整项目重跑，保留首轮失败。生成测试项目、GitHub 双系统 CI 及外部验收仍分别核对。

## 2026-11-03 f3c36f4 完整生成器回归与依赖预检

旧固定版本实际执行 3,036 个用例，2,767 通过、2 失败、267 条件跳过。两条失败都是原 Excel 工作簿合同。该探针直接从固定检出加载 `node_modules/xlsx/xlsx.mjs`；失败时该路径尚未安装。使用相同原探针在缺依赖的独立目录复现 `ERR_MODULE_NOT_FOUND`。放入锁定依赖后，同一测试程序集和同名两项重新执行，2 项均通过。原全量报告仍保留 2 条失败，复验不合并进原报告。Vue 编译器缺失是前文所列四项同步的另一处环境问题。

公开 `scripts/dev.ps1 test` 现在先检查实际工作簿和页面探针所需的 SheetJS、Vue SFC 编译器及 Babel parser。独立目录实测缺依赖会在 .NET 派发前拒绝并提示锁文件安装命令，锁定依赖齐全时能进入真实 .NET 测试派发。没有更改生产代码、原工作簿用例或跳过规则。当前完整回归、GitHub CI 及正式外部验收仍分别记录。

## 2026-11-04 9896573 十个后端项目完整本机回归

固定 `9896573799956a4888d2ae9d269839102c172e02` 的生成器项目完成 3,036 项，2,769 通过、0 失败、267 条件跳过。原 Excel 两个用例均包含在本轮全量通过中。把该项目与同提交的九个不同后端测试项目原始 TRX 逐份对照，十个项目合计 6,082 通过、0 失败、464 条件跳过。单独复验和旧基线不重复计数。

本轮使用 Windows 的独立检出、Release 构建及锁定前端依赖。跳过项包括未提供隔离 MySQL 配置、专属空库或正式浏览器租约的用例。归档在私有 `.local/release-packages-9896573/current-generation-full`。随后开发分支的安装文档、打包 README 和本地测试脚本变更单独留证，当前 GitHub 双系统 CI、正式 MySQL、跨端和性能仍分别验收。

## 2026-11-04 118395f 的双系统 CI 原始报告

运行 `41662997276` 的前端、Ubuntu、Windows 三个作业均已成功。读取 Ubuntu 实际上传的 11 份 TRX，后端十项目合计 6,081 通过、0 失败、465 条件跳过，采集宿主项目另有 23 项通过、零失败或跳过。生成器 2,768 通过、268 条件跳过。逐用例与本机 Windows 的 3,036 项生成器报告对照，唯一状态差异是验证 Windows 独占删除句柄的用例，Ubuntu 按适用范围跳过。原始制品和逐项目摘要归档于私有 `.local/ci-118395f-run-41662997276/ubuntu`。两套系统结果不相加，正式实库、跨端和性能仍另验。

Windows 上传的 11 份 TRX 显示后端十项目 6,082 通过、0 失败、464 条件跳过，采集宿主另有 23 项通过。两套系统逐用例对照，除 Windows 专用文件删除共享测试外，只有两个非有限数字用例的参数显示为 Infinity 或 ∞，两端结果均通过。Windows 制品归档于私有 `.local/ci-118395f-run-41662997276/windows`。


## 2026-11-05 c290277 基线CI终态

实读GitHub Actions run41904349745，对应c290277916698269db2565c30894b2d1c8798cea：backend(windows-latest)、backend(ubuntu-latest)、frontend三个作业均completed/success。它补齐该来源的CI子项，不将历史a86c43d的八项失败继续当作当前未修复结论。

随后V209实际SQL审阅发现普通文本LOCATE与原LIKE语义差异，V210／V211已修正代码及生成产物。新提交必须重新检查CI及固定包、真实HTTP／SQL／代表性性能，不能沿用c290277绿色。CI本身不证明真实设备、完整角色Runtime A或MySQL首版总验收完成。


## 2026-11-05 V235 修复原列表请求探针的共享合并接线

0c8c0bb 的运行 [41977100585](https://github.com/zhao-yc/metarill/actions/runs/41977100585) 已结束。前端通过，Windows 和 Ubuntu 后端均在第 8 步原页面固定请求检查失败。原平台列表使用共享合并函数后，隔离探针中的 yzp 仅含请求和提示替身，缺少该函数。

探针现在仍只装载实际共享模块的函数声明，并限制可取出口，原列表保存场景注入真实合并函数。19 页继续按三端与两视口执行。增加长 ID、显式 null、列表专用字段和选择态保持、详情专用字段隔离及零请求断言。原 CI 命令本机 12 项通过，实际 ASP.NET Core 原页面路由专项 10 项通过，均零跳过。新提交的远端 CI 结果须单独核对。


## 2026-11-05 e8a646d Windows 回环采集超时

[e8a646d 的 GitHub Actions](https://github.com/zhao-yc/metarill/actions/runs/41994937980) 中，前端作业通过。Windows 采集宿主作业的 34 项中 33 项通过，`Unexpected_database_is_not_reported_as_complete_evidence` 在读取回环 HTTP 响应时触发 10 秒客户端超时。日志未确定宿主未及时响应的原因。本机按相同 Release 命令和原门槛复跑采集项目，34 项通过、0 项跳过。保留远端失败，后续以新提交的实际 CI 结果为准。

## 2026-11-11 abbe89fb 当前候选 GitHub CI 终态

按固定提交查询对应的 PR 工作流，运行 42965876964 已成功结束。Windows、Linux 的 2 个后端任务和各 6 个生成分组，以及 1 个前端任务，共 15 项均完成且成功。CI 已列出两份后端报告和十二份生成报告，共 14 个可用制品。本轮归档工作流回执、任务结论及制品索引。

Server-200 独立生成器回归继续按 [V1045](current-linux-generation-batches-v945.md#v1045-当前候选前十七批复核) 复核。真实浏览器、MySQL 功能及性能证据分别见首版清单。[GitHub Actions 记录](https://github.com/zhao-yc/metarill/actions/runs/42965876964)

## 2026-11-11 e8bc7af1 GitHub CI 终态

GitHub Actions 运行 43030431808 对应 e8bc7af1，前端、两个系统的后端及十二个生成分组共 15 项全部通过。与固定发行源码 abbe89fb 逐路径比较，变化只在 docs/。abbe89fb 自身的 42965876964 同样核对 15 项通过。后续测试夹具调整及更新后的开发分支仍按各自提交核对。见 [e8bc7af1 的运行](https://github.com/zhao-yc/metarill/actions/runs/43030431808)。

## V1094 当前登录测试夹具的 Windows 定向复验

开发分支的唯一非文档差异是已提交的登录测试夹具冷编译等待。当前夹具的 Windows 和 Linux 各 11 项定向通过。开发分支完整 GitHub CI 继续按精确提交核验。见 [Windows 复验](current-windows-generation-full-v921.md#v1094-当前登录测试夹具的-windows-定向复验)。

## V1138 当前候选 GitHub CI

2026-11-12。固定源码 e682cd12 的 GitHub Actions 第 43073785233 次运行已完成。前端、Windows／Ubuntu 后端以及两系统各六组生成测试，共 15 个作业全部成功。[GitHub Actions 原运行](https://github.com/zhao-yc/metarill/actions/runs/43073785233)

下载的 14 份报告与 GitHub 制品 SHA-256 一致。两端生成器各 3,157 个测试 ID，按六组逐项核对冻结用例名称与计数。报告中的条件跳过均有原因。

| 测试 | Windows 通过 | Windows 条件跳过 | Ubuntu 通过 | Ubuntu 条件跳过 | 失败 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 生成器 | 2,884 | 273 | 2,883 | 274 | 0 |
| 后端 | 3,421 | 219 | 3,421 | 219 | 0 |

生成器的两端差异仅为 Windows 文件删除共享句柄用例。该用例 Windows 通过，Ubuntu 按系统条件跳过。前端按官方作业成功回执验收。Server-200 的独立 Linux 回归、真实 MySQL 和性能继续各自跟踪。

## V1190 日期修复候选 GitHub CI

2026-11-12。固定 b991b583 的 GitHub Actions 第 43124697441 次运行，前端、两系统后端及两系统各六组生成测试共 15 个作业全部成功。下载的 14 份报告逐件匹配官方 SHA-256。[GitHub Actions](https://github.com/zhao-yc/metarill/actions/runs/43124697441)

| 测试 | Windows 通过 | Windows 条件跳过 | Ubuntu 通过 | Ubuntu 条件跳过 | 失败 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 生成器 | 2,884 | 273 | 2,883 | 274 | 0 |
| 后端 | 3,421 | 219 | 3,421 | 219 | 0 |

生成器两系统各覆盖 3,157 个冻结测试 ID，同系统与 e682cd12 的逐项结果一致。两个实际宿主模板的 MVC 日期用例在两系统均通过。跨系统结果差异为 Windows 文件共享句柄用例，Ubuntu 按平台条件跳过。
