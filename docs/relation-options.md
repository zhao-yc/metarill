# List 关系候选分页与已选回读

本能力由 `RelationCompilation` 与 `BackendRenderer` 生成，只支持已有明确目标的 `List`。本轮仅新增后端合同，不表示现有选择器已经接入分页；SelectPage、MultiSelectPage、级联及关系派生不在本轮范围。现有生成应用须由生成流程更新后端，再由后续 UI 切片接入新入口；不能手工修改可覆盖 `.g.cs`。

## 固定路由与响应

设原数组地址为 `/api/admin/auto/{sourceTable}/{sourcePageId}/findNameId{TargetType}{sourceColumnId}`。以下地址全部通过生成时固定的字段、表和目标页产生，请求不能指定表、列、目标页、SQL、连接、排序或项目。

| 入口 | POST 请求 | 响应 data |
|---|---|---|
| 原地址 | 原 `parentId`、可选 `formParameters`；兼容已知 PageRequest 空配置 | 原 id/name/value/text 数组；超过 5000 条整体报中文错误，指向同地址 `/page`，没有截断数组 |
| 原地址 + `/page` | `pageNo` 默认 1、`pageSize` 默认 20 且最多 100、`keyword` 最多 100 字符、可选 `parentId` / `targetParentId` | 既有 PageResult：records/total/pages/current/size，records 每项仍为 id/name/value/text |
| 原地址 + `/selected` | `selectedIds` 字符串数组，原始数量最多 200；可选 `parentId` / `targetParentId` | 同四字段数组；重复 ID 去重，任何不存在、软删或越界项使整个请求失败 |

`id` 和 `value` 保持完整字符串长 ID。`total/pages` 沿用当前宿主的长整数序列化约定，`current/size` 为整数。`pageNo` 范围 1–1000000，防止无限或溢出偏移。候选固定按名称、ID 升序，名称相同时仍有稳定顺序；接口不接受客户端排序。

请求示例（在原地址后调用 `/page`）：

```json
{"pageNo":1,"pageSize":100,"keyword":"名称中的%_字符","parentId":"9007199254742001","targetParentId":"9007199254742002"}
```

已选项不跟随当前关键词过滤，使用 `/selected` 独立回读；它仍受全部访问范围约束，不能用来读取任意 ID。空数组返回空结果，父记录范围仍先校验。未知 JSON 字段被拒绝；旧数组入口保留原 `formParameters` 但不解释其为授权或 SQL。旧数组不接受非空 filters/orderBy/selectedIds，须使用明确的新合同。

## 权限与范围

三个入口都先要求来源页 list 权限、真实身份及目标页 list 权限。它们共用同一个生成查询，先排除软删除记录，再应用实际项目归属、目标页范围和控件范围；SQL 只投影 ID 与名称，不加载目标的大字段。

List 的页面覆盖按原已知配置键非空合并，原控件配置和未知合法键不会被删除。目标页若有已保存的 `linkConfigPageId`，必须是目标表实际存在且开放名称列读取的页面，不接受请求指定。未指定时保持原先优先同后台、无父范围目标页的选择方式。未移植的外部关系仍保持明确能力边界。

来源页的 `parentId` 与目标页/控件的 `targetParentId` 分别按各自元数据验证真实父记录存在、未删除且属于当前项目，均不作为授权，也不获取写锁。两个父定义可以不同，接口不猜索引→表等跨层映射。只有元数据证明来源与目标父表集合相同时，未传 targetParentId 才兼容原 parentId；显式空字符串仍拒绝。双父关系缺少 targetParentId 会整体报中文错误并指向分页合同，旧数组也不能静默忽略目标父范围。目标表须有项目归属或明确 global 声明；父表必须有项目键。当前仅编译已支持的身份/父键等值范围，不支持的 whereSql 表达式在生成前拒绝，不能忽略条件生成宽范围查询。

关键词通过 FreeSql 强类型 Contains 表达式参数化。MySQL 实测包含 `%`、`_`、引号的名称按字面子串匹配，不能改变 SQL 结构；这项证据不替代其它数据库适配验收。

## 本轮验证与剩余边界

真实生成样板使用独立 Server-200 MySQL 测试库及随机三表：5014 个有效候选，另加外项目、软删及另一父范围目标。回归覆盖分页/count、重名 ID 排序、非首屏已选回读、旧数组整体超限、混入任一非法项整体拒绝、双方页面权限、真实父记录校验及严格输入。另一个来源页使用不同父表，真实验证双父独立查询、缺少目标父/两个 ID 混用/外项目/软删拒绝。真实 HTTP 宿主只替换测试认证提供者，执行实际生成 Controller、Service 和 FreeSql 查询；普通平台认证依平台既有验收，不把测试固定身份当正式认证。

平台已有 config_index_column→config_column 的已存目标页分别受索引父级和表父级约束；本合同保留两者定义并要求分别提供，当前专用索引编辑器不依赖该通用只读列。既有主框架合法元数据仍可整批生成，不能以省略目标页范围消除双父问题。

本轮不改 uni-app 模板、控件、独立包宿主或元数据，不提交旧生成产物。现有 UI 仍调用数组接口，超过上限会明确报错；分页搜索/已选回显 UI、SelectPage/MultiSelectPage 和独立应用完整前端验收需后续切片完成。

本次最终执行 `dotnet test backend/tests/Metarill.Generation.Tests/Metarill.Generation.Tests.csproj`：43 项通过，2 项专用全新库测试因本轮未启用独立创建库配置而跳过，零失败；其中本切片新增 4 项均实际执行通过。空库权限与初始化由独立验收切片继续负责，不能将这里的跳过计为通过。没有运行 Docker，也没有执行真实生产变更。
