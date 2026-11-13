# 可信安全模块的隐藏项目候选

本片仅补 `security-management-v1` 内后台类型页的旧候选合同，不接默认Full发布、工作台、初始化或Agent，不改变模块元数据、原DOM/CSS、表结构和通用 `RelationCompilation`。

## 原合同与实际隐藏状态

固定来源是表 `1696799308939239425`、页 `1696799309006348290`、项目列 `1696799602636988418`、页字段 `1696799602699902978`。原源页将项目字段的编辑/列表/筛选标志全部关闭，初值为 `current_yz_project_id`；原详情没有显示该下拉。原Java仍生成 `findNameIdYzProject1696799602636988418` POST，并只选项目 `id/name`。因此这里恢复的是隐藏字段的后端合同，不为消除待办而打开新控件。

原 `PowerInterceptor` 将 `findNameId` 作为源页查询、新增、修改任一权限的辅助动作。本片复用 `RoleCheckboxAuthorization` 两种明确拒绝合同，保原宿主401/403分流，不增加目标项目CRUD页query权，也不使用平台超级权限兜底。

## 受信生成与请求范围

`TrustedSecurityProjectOptionsCompilation` 先消费三partial编译器的工厂签发完整快照，再校验固定表页列、隐藏声明、原List五键以及项目Id/Name/DeleteNo物理图。普通 `Generate(catalog,...)`、业务JSON标记或目录修改不能启用此计划。

后端只有三个窄接点：Service与Controller各追加一次renderer；构造与注册共享的授权判定追加一个谓词。平台普通生成路径和多项目关系规则不改。

原路由保留，并提供既有分页/已选外形：

- `POST /api/admin/auto/yz_admin_type/1696799309006348290/findNameIdYzProject1696799602636988418`：ID省略返回当前项目数组；显式null/空字符串返回空数组；明确外项目ID拒绝。
- 同路由 `/page`：使用既有 `pageNo/pageSize/keyword`，只对当前项目匹配。页码大于1时records为空，total/pages仍按0或1计；关键词使用数据库参数化Contains。
- 同路由 `/selected`：使用既有 `selectedIds`，空选择不退化为全部项目；只允许当前项目，仍验证真实项目行。

三个入口均输出原 `id/name/value/text`，长ID沿现有字符串转换。未知、重复、过大请求或query字段拒绝；本字段不使用非空parent/targetParent；formParameters只保原槽，不解释成授权、目录或SQL。

## 真实身份和SQL边界

Service必须从同一个Scoped Full授权对象取得 `IRuntimeAuthorization` 与 `ISecurityIdentityAccessor`：持久会话ID为正、非Agent、非平台管理员，actor及principal项目和后台都必须与编译包一致；单独RuntimeActor、开发无持久身份接口、匿名或变更身份均在查询前拒绝。

候选自己的SQL只按固定项目主键、软删除和可选关键词查询，并且仅投影 `id/name`，结果至多1条。项目缺失/删除直接拒绝，不用编译名称回填，不查连接、密码或其它项目。普通查询一次；非空关键词最多再进行一次同范围两列查询。当前项目的真实名称变动正常回显，项目表兼容的 `yz_project_id` 列不作为身份归属条件。

这不等于整个认证请求从未读取项目连接定义：现有 `AuthenticationService.AuthenticateAsync` 仍通过 `SqlRowStore.ReadAsync("yz_project")` 读取原项目行，本片没有改这条共享认证路径。认证窄投影属于另一项改进，不能用候选SQL证据替代。

候选成功不是写入证明。项目字段仍由生成Service的真实actor填入，客户端没有该DTO写槽；提交继续由真实A协调器按项目→后台锁序、同一Session/事务当前读及最终合并实体校验。项目在候选后删除，提交应拒绝并完整回滚。

## 验证边界

测试使用实际可信生成程序集、原Full授权实例及实际协调器，传输只在内存执行固定SQL/表达式；真实FreeSql在CurdBefore捕获实际SQL和DbParms后中止，连接Open必须为0。另验证原前端逐字不变、普通路径不启用、冻结篡改拒绝、原四字段/长ID/空值/分页/关键词、仅新增或修改权、匿名/Agent/开发/外realm拒绝及提交时项目删除回滚。

没有执行MySQL、启动HTTP或作浏览器验收。Runtime A、工作台/初始化、五Custom组件和默认Full门禁均保留，不能将本片当作完整安全管理已发布。
