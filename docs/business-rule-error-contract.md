# 显式业务规则的原错误协议

2026-10-28，B99。原 Java V8 固定样板的 `GlobalExceptionHandler.handleException` 通过 HTTP 200 返回 `R.error(message)`；手写金额规则提示为“合同金额不能小于零”。原 C# 完整包把同一 `ArgumentException` 归入 HTTP 400 和通用输入提示，不能作为该业务规则已兼容的证据。

## 本批实现

`Metarill.Core.Contracts.BusinessRuleException` 由业务代码主动声明可展示提示；完整包异常边界按原 HTTP 200、`code=500` 和该提示返回。平台主服务已有对应的 InvalidOperationException 原协议处理，新类型继承这一边界。

```csharp
using Metarill.Core.Contracts;

// 金额属于受维护的业务规则，提示可直接用于原页面。
if (entity.Amount is {} amount && amount < 0)
    throw new BusinessRuleException("合同金额不能小于零");
```

完整应用的类型错误、缺身份、权限不足、暂不支持能力、未知内部异常继续现有状态和脱敏规则：

| 类别 | HTTP | R.code | 提示来源 |
| --- | --- | --- | --- |
| 显式 BusinessRuleException | 200 | 500 | 业务代码主动声明的提示 |
| ArgumentException / JsonException | 400 | 400 | 固定参数提示 |
| UnauthorizedAccessException | 401 | 401 | 固定身份提示 |
| SecurityException | 403 | 403 | 固定权限提示 |
| NotSupportedException | 501 | 501 | 固定能力提示 |
| 未知内部异常 | 500 | 500 | 固定内部错误提示，异常链写日志 |

新类型不接受内部异常链。业务提示与数据库/IO/未知异常分开声明，宿主不从数据库异常正文推断“可展示”。导入输入分类和导入内部执行包装继续各自合同。

## 更新已有独立包

先从当前源码生成完整候选包，更新 Core 和宿主模板，再检查自己的手写 partial/Business。希望保留原业务提示的规则改用上述类型，增加命名空间引用；生成器继续保护手写文件，不能自动覆盖旧业务实现。严格编译并核对错误 HTTP、业务码、提示及事务回滚后再切换。

首版尚不承担旧项目自动迁移。本批只证明显式业务提示子集，普通生成校验、权限、导入和全部自定义接口的 Java/C# 异常响应仍需逐项对照。

## 实际验证和边界

真实 FullApplication 临时宿主在 Production/Web 模式下启动本机回环，用仅存在于测试包的异常探针验证上表全部类别及原控制器直接 R.error 返回；参数、内部、身份和权限的测试标记不进入响应。该探针不建立数据库连接，也不替代真实身份。两个实际生成 Service/partial 用例核对原金额提示、0/null、保留标记和目录外身份拒绝。3 项回归通过，零跳过；独立于实库准备助手的宿主用例再次通过。平台API 68通过/47实库跳过，Agent 226通过/25实库跳过，严格编译通过；正式CLI两轮重生成705个已跟踪平台/示例/原前端文件逐字不变。

这不是正式三表 CRUD/事务或 Server-200 验收。此前 `2cc4c95` 归档及失败证据保留；B98 原计划先更新固定产品包，再准备新租约，没有执行旧包实库验收。进度以 [总表](project-progress.md) 和 [全局来源](global-cached-source.md) 为准。
