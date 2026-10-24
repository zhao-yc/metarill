# 原选择页名称筛选编译

本切片只交付独立编译模型、原辅助名共享及测试，不改 `BackendRenderer`，不声明 HTTP 名称筛选或任何数据库 JSON 方言已完成。

## 原合同与当前缺口

原 `UniappService.GenerateList` 的 SelectPage/MultiSelectPage 文本框提交稳定辅助名称键（如 `groupName` / `targetNames`）。原 `JavaService` 对冗余名称列做 like，非冗余 Multi 另用分页前 EXISTS。当前 C# 原列表也发该键，但普通 Query 仅编译 page.Searchable 的源 ID 键，因此辅助名称尚不能按原交互筛选。

C# Multi 名称是 JSON 数组文本，默认序列化可能转义中文。不能直接对序列化 JSON 字符串 Contains 冒充“任一名称匹配”；真正 JSON 方言必须解码数组成员并在分页/计数前匹配。

## 接线入口

`SelectionNameFilterCompilation.ForPage(model, table, page)` 返回：

- `Bindings`：固定 Source、StorageColumn、FilterKey、Kind（ScalarLike / JsonArrayAnyLike）。列和属性只来自该批受控元数据。
- `Gaps`：来源、请求键、明确原因；不猜字段、不回退到任意表或范围。
- `ConsumedKeys`：成功绑定的名称键。接入时只排除这些键的重复普通 case，保留其他原 ID 筛选合同。

`RenderCases(plan, renderJsonPredicate)` 生成完整 switch case：值仅接受字符串，null/空白不增加谓词；单值使用强类型 Where/Contains。JSON case 必须由调用方提供真实数据库适配分支；没有实现即拒绝。存在 Gaps 同样拒绝，避免名字键落到错误的普通字符串分支。回调只用于可信生成器接线，不能来自 HTTP、SQL 配置或用户脚本。

后端应先编译 plan、处理能力缺口，再将生成 case 放在既有 Query switch 中，并消除 ConsumedKeys 的重复 generic case。Page、Count、Export 必须复用这一 Query；不能在分页后拉全表或内存过滤。所有原 actor/project/parent Scope 和权限保持在同一读取入口。

## 已证明子集

- 冗余表，源 Select/Multi 字段已在当前页开放筛选。
- 原表级辅助名称确实声明为本页可读物理 string；联接/虚拟/隐藏或缺失名称不接受。
- 固定目标页属于明确目标表，目标名称为已开放 string；目标有项目键或明确 global 声明。
- Multi 必须通过既有 MultiSelectCompilation，且 Names 与稳定原辅助列一致。
- 页级 nameField 改变稳定名称取值、单选附加 where/order、未知映射、多来源争用同一别名、非冗余等保留明确缺口。nameField=id 属于独立 ID 合同，本切片不将它伪装为名称匹配。

`SelectionDisplayFieldCompilation.Resolve` 是原列表和新编译器共享的辅助属性规则；只返回已声明字段或原 ID 回退。页面覆盖不会重命名表级辅助属性。

## 验证范围

测试实际生成原 Vue 并由 Vue3 编译挂载，确认名称键和冻结请求；单值 case 加入真实生成 C# 项目并编译。缺列、隐藏、联接、类型、非冗余、目标归属/投影、覆盖、复杂范围和别名冲突均拒绝；JSON 无真实适配器不能输出降级分支。本切片没有访问数据库，没有运行 HBuilderX，也未完成真实 HTTP 筛选验收。接线后仍需 Server-200 隔离库验证 page/count/export、中文/引号/转义、分页和跨项目/父范围。
