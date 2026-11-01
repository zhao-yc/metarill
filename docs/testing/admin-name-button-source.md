# 固定后台名称回显保留业务按钮授权来源

日期：2026-11-02。编号：C97。起点：`d5150147b460583bc4cef261fe46a30acc5d68a9`。产品、回归、正规再生成与进度同任务提交。

## 实际问题

沿C96检查按钮组合时，以原角色／后台固定元数据生成真实Service，使用只有业务按钮权、没有普通页面编辑或查询权的主体执行按钮更新。更新核心已经使用按钮授权，但写后名称回显重新调用普通页面的update权限校验，实际抛出UnauthorizedAccessException并由DatabaseSession回滚。首个红灯为0通过／1失败，失败栈明确位于名称回显的页面授权，不是生成或编译失败。

业务按钮是独立资源，原合同不要求额外页面query/update。修复应保持该来源，不能通过给账号增权、放宽页面权限、移除名称或跳过事务来绕开。

## 生成源头修复

仅在已证明固定后台名称、当前用途允许回显且页面发布了业务按钮时生成专用私有回显方法。普通更新保留原分支，按钮更新使用服务端写核心传入的固定按钮ID；客户端没有新增选择槽。

按钮回显首先复用既有RequireButtonCurrent方法，再次核验真实Actor、已发布按钮和同一写事务的当前权限。该检查发生于观察者之后，拒绝会使整个DatabaseSession事务回滚；不能仅凭进入请求时的旧快照继续返回名称。

目标仍只读当前项目、未删除后台的ID/name；空后台不读目标，非法ID拒绝，外项目或已删除目标的名称为null。Entity、DTO、既有VO结构、普通页面授权、候选及原按钮业务写入流程不变。未新增路由、接口、依赖、物理字段或公开任意查询方法；不调整超级角色或其它角色分配政策。

## 实际验证

新增两类固定宿主共用一次真实生成编译的26个用例，全部通过、无失败或跳过：角色和配置表的按钮单独授权成功、同一事务三处当前证明、目标为空／外项目／已删除、角色原必填拒绝、未知按钮、换Actor、请求前撤权、锁前／写前／观察者后当前证明拒绝、普通更新权独立，以及观察者实际撤权后停止名称读取并回滚。

测试调用真实生成Service、Mapper及DatabaseSession，表达式由真实FreeSql翻译，仅终端行／写传输和数据库连接事务使用受控替身，禁止网络连接。因此写调用计数和事务commit／rollback是离线执行证据，不能当作真实持久化、锁等待或并发证明。

标准拒绝用例直接调用未安装宿主守卫的Service，在回执阶段拒绝并回滚，写传输可能已经触发。它仅证明标准分支不借按钮权，不能声称HTTP写前已经在该用例中验证。真实宿主仍须保留原Controller／写守卫的前置授权。相邻回归另外覆盖既有生成按钮写核心、按钮记录范围及实际完整包授权类。

最终同一产品的相关生成组合为174通过、0失败、0跳过，包含上述26项及既有固定名称、用途IR、已发布投影、旧写合同、固定查询、按钮更新、按钮记录范围、实际完整包授权与当前完整管理包构建／匿名边界。不与早期红绿或26项相加，也不代表全后端或远端CI全部通过。

平台真实HttpRuntimeAuthorization另1项通过、0失败、0跳过，验证固定按钮归属、普通页权独立及范围值；该项是直接执行真实授权类，不是本轮真实登录HTTP。

正式CLI严格Release构建为0警告／0错误，平台和示例各生成两轮，215文件逐字稳定；24份手写partial、499份原前端文件及9份元数据保持。仅平台ConfigPage／ConfigTable两个生成Service及其清单共3项受管变化，生成前端也没有变化，原能力缺口清单保持。没有手改生成产物。

可复核的核心命令：

```powershell
.tools/dotnet/dotnet.exe test backend/tests/Metarill.Generation.Tests -c Release --nologo --no-restore --disable-build-servers --verbosity quiet -warnaserror --filter "FullyQualifiedName~FixedAdminNameButtonUpdateTests|FullyQualifiedName~FixedConfigurationAdminNameTests|FullyQualifiedName~FixedRoleAdminNameTests|FullyQualifiedName~ReadProjectionCompilationTests|FullyQualifiedName~PublishedReadProjectionCompilationTests|FullyQualifiedName~LegacyPageWriteContractTests|FullyQualifiedName~FixedPageQueryCompilationTests|FullyQualifiedName~GeneratedBusinessButtonUpdateTests|FullyQualifiedName~BusinessRecordScopeGenerationTests|FullyQualifiedName~BusinessButtonApplicationAuthorizationTests|FullyQualifiedName~Full_management_package_builds_from_its_own_sources_and_keeps_original_anonymous_boundaries"
.tools/dotnet/dotnet.exe test backend/tests/Metarill.Api.Tests -c Release --nologo --no-restore --disable-build-servers --verbosity quiet -warnaserror --filter "FullyQualifiedName~BusinessButtonPlatformAuthorizationTests"
.tools/dotnet/dotnet.exe build backend/src/Metarill.Cli -c Release --nologo --no-restore --disable-build-servers -warnaserror
.tools/dotnet/dotnet.exe backend/src/Metarill.Cli/bin/Release/net10.0/Metarill.Cli.dll generate --metadata backend/metadata/platform.json --output backend/generated/platform --namespace Metarill.Platform
.tools/dotnet/dotnet.exe backend/src/Metarill.Cli/bin/Release/net10.0/Metarill.Cli.dll generate --metadata backend/metadata/demo.json --output backend/generated/demo --namespace Metarill.Demo
```
## 失败记录与边界

扩展观察者夹具首次误用接口方法名，编译器拒绝，未运行用例；之后精简目录遗漏原排序字段，整个夹具初始化失败，两次26失败不能算成26种业务故障。读回原排序并补回insert_time后为23通过／3失败：角色空后台被原必填校验拒绝，另两例错误期待未装宿主守卫的标准Service在写传输前拒绝。仅将这些断言校准为原必填的精确错误及真实Session回滚，产品未为夹具放宽规则；最终26通过。全部原始日志及TRX私有保留。

本轮不重复记入C96原浏览器和真实MySQL读取验收；该结果仍属于其明确基线。真实普通账号按钮／撤权／文件回滚、真实数据库并发、其它显示名称组合、原浏览器写入和性能仍须继续。未消费任何待批新租约，不解除公开Full或MySQL首版总门槛。

## 升级与回退

更新生成器后正规再生成受影响Service及清单，独立完整管理包须重新打包和构建，保留原手写partial。原管理端及生成前端不需要变化；无DDL、数据回填或写入DTO迁移。回退使用相互匹配的生成源头与受管产物，不撤销业务数据。
