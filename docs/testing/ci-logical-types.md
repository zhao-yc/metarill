# CI 逻辑类型合同与重复运行治理

日期：2026-10-30；起点 `830d0ed677c2ea9ae94ccde8955e8aaa132129aa`，本任务同提交。对应 C48。原前端、生成模板、运行时代码和数据库均未修改。

## 已复现的问题

旧提交 `2e70726c619d949bd4913a0e93e7551febe84694` 的 Windows 与 Linux CI 均在 `LogicalTypePartialCompatibilityTests.Compatibility_is_exactly_fixed_platform_page_and_does_not_change_original_metadata_or_ui_required_rule` 失败。测试仍要求 `time`、`binary` 导致未知逻辑类型异常，但生成器已分别支持 `TimeSpan?`、`byte[]?`。本地当前基线也单项复现失败；相关生成回归在修正前为14通过、1失败、零跳过，失败项相同。

修正仅调整测试：两个已支持类型逐项检查强类型映射及输入逻辑类型保持；`geometry`、`xml`、`invalid` 继续断言未知类型拒绝并核对异常内容。固定平台/表/页面/字段身份、原页面必填规则，以及真实生成 Service 的局部更新、创建、复制、手写钩子和事务回滚测试均保留。

## 工作流范围

增加工作流级 `concurrency`，同一来源仓库、引用类型、来源分支及目标分支只保留最新运行。当前同仓库开发分支的 push 与指向默认分支的 PR 共用组；不同来源仓库、标签或不同目标分支分别隔离。旧工作流没有并发组，不能假定新配置会自动取消那些运行。

逐字核对移除新增并发块后与原工作流一致，全部平台矩阵、步骤、测试项目枚举、最终失败判断、产物上传及只读仓库权限均保留。没有通过删减测试消除失败。

已对13个旧工作流逐一核对仓库、来源分支和当前基线祖先关系后取消，并通过独立 GitHub 读取确认其结论为 `cancelled`；当前基线的两个运行保留。取消记录不是成功结果，双平台旧失败历史继续保留。

## 本轮验证与剩余范围

- 修正后 `LogicalTypePartialCompatibilityTests`、`BinaryGenerationTests`、`ClockTimeGenerationTests` 合计15项通过，零失败、零跳过；包含实际生成 Service 与独立完整包编译运行。与修正前使用同一筛选范围。
- 用户再次确认无损路线后，`CrossTableExcelPathTests`、`CrossTableExcelRowTests` 共45项复验通过，零失败、零跳过。这是共享合同验证，不包含真实 SQL、正式身份、文件选择或数据库事务。
- 生成测试项目 Release 严格编译 `-warnaserror`：零警告、零错误；`git diff --check` 通过。
- 完整 GitHub Windows/Linux/前端工作流须按新提交 SHA 另行核实，局部测试通过不等于完整 CI、MySQL 首版或真实设备验收通过。

私有证据位于交付根 `.local/ci-concurrency-20261030`，包含修正前后日志/TRX、工作流范围逐字验证、13项取消结果和双平台失败摘录。没有配置写入、DDL/DML、容器操作或部署；B102、C38、C47 的待确认精确范围没有执行。
