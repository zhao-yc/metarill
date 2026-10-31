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
