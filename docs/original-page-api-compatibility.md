# 原 V8 页面首批 C# 接口兼容

本切片仅调整后端生成源、Core 合同和既有权限/文件更新开关，不修改原页面或前端生成模板。原页面 PC/移动端布局、组件及整实体回传继续保留；菜单单表树是已经明确的结构调整，不在本合同恢复旧双菜单表。

## 完整实体回传

原详情 `getEntity` 把整个 VO 赋给 `entity`，保存和删除直接提交该对象；新增初始 `id` 为空字符串，子页保存前回填父外键。生成 Controller 在真实页权限及 Actor 验证后，使用编译为常量的 `LegacyPageWriteContract` 把旧请求转成原严格 DTO，再调用原 Service。

- 只有固定页可写字段进入 Create/Update DTO；删除仅保留 `id`。
- 当前页已声明可读字段、常量 `*Show` 和已知只读辅助名称只作回显并丢弃，不能覆盖服务器保存值。Multi 名称继续由源事务派生，省略 IDs 不刷新历史名称。
- 新增 `id` 只允许 null/空白，不能指定主键。实际主键仍由服务器分配。
- 父实体键只能等于本页真实 `parentId` 上下文，项目键只能等于真实 Actor 项目；未知字段、重复字段、伪项目和跨父均拒绝。客户端不能提交白名单、表名或 SQL 改变合同。
- DTO 的 `Optional` 仍记录省略与显式 null；HTTP 适配使用宿主相同 JSON 转换器。原 DTO 自身仍拒未知字段，手写 Service/MCP 不依赖这个 HTTP 归一器。

生成写 Action 使用固定 `LegacyPageWriteRequestAttribute(typeof(严格DTO))`。现有 Agent/MCP 能力目录从此标注生成原 DTO schema，保留 `additionalProperties:false` 与可选字段/null，不把 Controller 的原始 JSON 输入公开成任意对象。项目当前没有 OpenAPI/Swagger 入口；未来加入时也应复用同一标注。

## 候选与排序

原选项 `findNameId<来源字段去Id或Ids后缀><列ID>` 路由保留，已有按目标表名派生的路由作为同 Action 别名继续可用；分页和已选端点同样保留两种固定命名。目标表、列、页、控件范围由元数据编译，不能来自请求。

本轮仅覆盖已安全编译的 List 关系，未把 MultiCheckBoxDatabase、Cascade 或未实现表达式泛化成通用候选接口。旧数组端点接受空 POST、`{formParameters}` 和只读字段的 `{id,formParameters}`。明确 `id` 走已有同范围已选回读；显式空 id 返回空数组，不退回全量候选。外项目、软删或范围外 id 整体拒绝。返回仍是 `id/name/value/text`，64 位 ID 保持字符串。有父范围的旧请求仍需明确传页面已有 `parentId`（不改变 UI），不能因空 POST 丢失父限制。原 `formParameters` 仅作为已知请求键接收，本轮不解释未实现的动态表达式；无法安全编译的条件继续明确拒绝。

来源页和目标页仍使用现有权限校验，本轮没有通过放宽查询权限补偿未完成的授权继承。数组超过 5000 条仍明确要求分页，不截断。原 `uni-th` 取消排序发送 `order:null` 或空字符串时回到固定默认排序；未知非空方向仍拒绝。

## 更新与不分页开关

`PageUpdatePolicy.CanUpdate` 集中解释原 `isUpdate` 和 C# `isSave`。原显式 `isUpdate` 优先，因为 DTO 默认 `IsSave=1` 且再序列化会输出它，不能把该默认值误认为用户显式配置冲突。非法值拒绝。后端方法生成、子页能力、角色动作资源和文件上传统一使用该规则；前端生成器由完整恢复切片使用同一规则。

仅服务器已发布元数据 `PageSize=0` 的页面允许 `pageSize=0`，且请求页码必须为 1；在数据库查询前检查。该分支不添加 LIMIT，完整返回所有满足原页面范围及筛选的记录，不静默截断；`size=0`、`pages=0` 保留旧不分页响应。普通页面传 0、负数或超限分页请求继续拒绝。大型不分页页面的资源使用由明确配置负责人承担，不把客户端 0 当作任意页面绕过限额的方法。

## 验收与下一步

使用 Server-200 明确隔离 MySQL 配置、随机三表夹具测试实际生成 Controller/DTO/Service：完整实体新增/更新/删除、空新增 ID、只读列、常量回显、父键、伪项目/未知/重复字段拒绝、省略/null、候选原新别名和空 POST、外项目/软删拒绝、排序取消及 24 行不分页。夹具包含参数化预置数据，不冒充浏览器操作；结束仅删除本轮随机表并反查不存在。既有 Multi 真事务测试同步覆盖原完整实体名称回显、服务端派生及文件引用失败不变。

本切片不宣称以下能力已完成：原子页/选择页有效授权继承、无认证头的原导出 URL、列表与详情分投影、统计、关系名称筛选及非冗余读计划。应继续在 C# 安全边界补齐，不能靠简化原页面或忽略范围规避。前端完整还原和 HBuilderX/浏览器验收由对应切片及协调者执行。


2026-10-20 本普通 clone 验证：Generation 完整 138 项中 136 通过、0 失败、2 项 FreshInstall 专属配置未提供而跳过；这两项不计首次安装验收。定向 14 项含实际生成 HTTP/MySQL/Multi/选项回归全部通过；Agent 实际 MVC schema 1/1、Files 更新开关真实 MySQL 1/1 通过，均零跳过。测试配置的 MySQL 驱动主机解析已核对 Server-200，业务库精确为隔离测试库。私有 TRX 位于 `.local/original-page-api-compat/`，不提交测试配置、日志或夹具。此结果不替代完整原页面 HBuilderX/浏览器或真机验收。


## 旧按钮别名的实际MVC路由（2026-10-20）

真实MySQL流程发现config_page/copy_page旧POST入口返回404，原sys/config_button/execute网关仍能工作。原因是三条兼容路由把URL末段命名为MVC保留值action，与Legacy动作的固定必需值冲突；现只改为operation占位名，原URL、请求、数据库按钮白名单、管理员和项目校验均不改。

最小ASP.NET TestServer通过真实MVC端点选择核对8个旧URL；修前5条动态别名404、3条固定/网关匹配，修后8/8通过、0跳过。测试在实际动作执行前停止，不注册数据库或替身Business，因此只声明路由匹配，不替代复制/生成授权或真实事务验收。红/绿证据为.local/original-alias-route-before.trx与original-alias-route-after.trx。

## 2026-10-31固定请求补漏

C63从原110源码按三个条件编译集合盘点62组固定方法/地址，实际Program探针复现4处404；本轮恢复首页搜索、账号候选及旧二级菜单候选地址，保留原响应和真实页权限，仍余批量运行时接口未实现。旧菜单别名指向单表菜单，不恢复旧双表/普通权限表写入口。具体回归、原失败与未验收边界见[原接口恢复](testing/original-frontend-route-restoration.md)；批量逐项目在线助手方案已确认，不能将排队计为同步完成。
