# CI兼容测试夹具跟进（2026-10-29）

## 实际失败

固定提交 `fb44b13f9ed8058eedf040186016b9fbb30b684e` 的两组前端检查成功、四个后端检查失败。已读取运行 [40792040390](https://github.com/zhao-yc/metarill/actions/runs/40792040390) 的 Linux／Windows 原始诊断，两者均在 `Metarill.Compatibility.Tests` 编译阶段报告五处过期调用：

- `CompatibilityButtonTests.cs`、`ConfigurationActionsFixture.cs` 两处未给兼容业务传入必需的执行租约守卫。
- `CompatibilityButtonTests.cs` 两处仍按旧构造器创建中央按钮控制器，没有实际 `OriginalBusinessButtonExecutor`。
- 同文件一处仍从 `IActionResult` 直接取 `.Data`，未按实际单层 `OkObjectResult → R<CompatibilityButtonResult>` 断言。

原失败运行和报告保留，不能将前端成功写成整个CI成功。另一组运行 [40792048349](https://github.com/zhao-yc/metarill/actions/runs/40792048349) 的三项检查终态也已核对；其原始编译日志未在本批逐项重复展开。

## 修正

仅修改测试夹具和CI驱动，生产构造器及权限/记录/租约约束不变。集成业务使用实际 `DatabaseButtonExecutionGuard`，协调连接显式限定为夹具已有隔离会话，不把这项局部夹具声明为平台到业务库协调矩阵通过。

兼容控制器使用真实中央执行器，夹具明确没有手写业务处理器或动作目录，也没有可用的原动作身份；意外进入显式动作执行租约会直接失败。该边界只验证原兼容白名单，显式动作的真实完整包/记录/HTTP合同见C21，不能用空依赖或空实现假装原动作已经验收。服务容器由夹具负责释放。

原集成测试仍要求显式实库配置，本批清除所有 `METARILL*` 测试环境，不连接Server-200、不创建账号/库/表或执行实际租约。不能把编译通过算作这些集成路径已运行。

## CI完整执行门槛

原循环在首个项目失败时立即抛错，后续项目可能没有执行。现按完整路径稳定排序，逐一运行全部后端测试项目，收集失败项，最后任一失败仍抛错并保持整个job失败；没有添加 `continue-on-error` 或放宽断言。

本地控制流验证直接取实际YAML的PowerShell脚本块，以独立进程和三个自有临时项目替换 `dotnet` 调用：中间项目失败时三个项目都执行且最终退出1；全部成功时三个项目都执行且退出0。这只证明CI驱动控制流，不作为产品测试或实库证据。

## 固定来源与结果

普通Agent记录写接线仍在另一组未提交文件中，未混入本次CI修复。验证从 `fb44b13` 加本轮暂存文件的Git索引树导出独立来源，不消费该未完成接线。源码树、测试TRX及原远端错误摘要保存在本机忽略目录。

```powershell
dotnet test backend/tests/Metarill.Compatibility.Tests/Metarill.Compatibility.Tests.csproj -warnaserror --logger 'trx;LogFileName=compatibility-final.trx'
```

首轮固定来源实际严格编译/运行：18通过、11实库条件跳过、0失败。CI驱动的失败/成功两种控制流均通过。最终源文件核对及新提交的Windows/Linux远端复验另记；未执行项目不能用其他项目结果替代。

## 后续Linux实际失败与精确范围夹具修正

本次读取[aba4d5e的push运行40806534633](https://github.com/zhao-yc/metarill/actions/runs/40806534633)终态日志：前端成功，Linux后端失败；同运行Windows检查读取时仍在执行，不能视为通过。Linux全部项目实际跑完，生成测试2039通过、252跳过、1失败，其余九个测试项目无失败。唯一失败为 `ExactDataManageGenerationTests.Derived_match_compiles_as_hidden_read_dependency_and_separate_lock_plan`，异常为TargetParameterCountException；不是其原本要求检查的内部事务拒绝。

生成方法跟进派生规则预检后，新增可选参数exactLocking=true，反射Invoke不会自动省略实参数组长度。旧测试仍传三个参数，尚未进入真实事务检查。只修正该测试：先明确断言第四参数名称、可选标志及默认true，再传Type.Missing使用实际默认值，保留TargetInvocationException内层InvalidOperationException和“真实写事务”断言。生成器、产品权限、锁及数据库代码均未更改。

本地Release执行整个ExactDataManageGenerationTests组，8通过、0失败、0跳过；缺事务必须拒绝的实际生成程序集断言已运行。日志/TRX保留在私有 `.local/exact-scope-ci-20261029`。这只关闭已知反射参数回归，不宣称全部生成测试、Windows/Linux最新提交CI、实库锁或最终首版验收完成。新提交仍须按具体SHA跟踪远端CI。
