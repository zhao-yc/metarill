# 原权限工作台协议恢复

本片只恢复共享 `Metarill.Security.RolePermissionBusiness` 的原展示合同和目标角色草稿操作；不修改前端，不接独立 Host 路由，不启用可信模块、不解除 Runtime A / Full / Custom 门禁，也不包含账号初始化。

## 原源码证据

- 原 `pages/main/set_role_permission.vue` 保留 `roleId` / `id` 导航和离开前确认，实际工作台消费 `GET /admin/sys/yzp_permission/roles`、`POST snapshot` 的 `{roleId}`、`POST save` 的 `{roleId,revision,grants}`；`grants` 为 `menuIds`、`pagePermissions`、`buttonIds` 三个独立维度。
- 原 `yzp-permission-workbench.vue` 的 `hasMenu` 和页面表名展示需要菜单 `targetPageId`、页面 `tableId/tableName`；现在这些值仅由服务端完整目录的真实引用派生，ID 仍为字符串，不解析路由或信任客户端名称。
- 原 Java `YzpPermissionManagementController` 直接调用 Business。`YzpPermissionManagementBusiness.save` 经 `lockRole` 验证真实同后台与原角色页修改权，再经 `writeDocument` 的同事务当前角色许可调用 Service；`YzRolePermissionServiceImpl.beforeSave` 复用 `beforePermissionSave`。这条链没有目标角色 `isSuper=1` 时禁止保存的规则。原组件也明确提示：修改资源授权不会撤销该角色的超级权限。

## 最小后端变化

`Snapshot` 补齐纯 VO 字段：菜单 `targetPageId` 来自 `MenuMetadata.ConfigPageId`，页面 `tableId/tableName` 来自真实页表引用。内部表名映射仅供展示，不进入原 revision 预像。

删除 C# 额外的“不能保存超级角色资源”拒绝。操作者仍需当前同后台 super 或原真实角色页 `is_update/is_all`，目标后台/项目、锁后当前身份、目录证明、旧 revision、资源与动作白名单、聚合锁序和全部事务写入不变。此保存只写 `yz_role_permission`，不能改 `yz_role.is_super`，也不能把目标角色加入操作者账号。查询权与修改权继续分开，保存回执不额外要求查询权。

原 Java 的 `activate` 仅用于 LEGACY 文档迁移。C# 新库仍固定 `mode=RESOURCE`，本片没有增加激活、旧菜单授权或危险初始化接口。

## 验证及边界

2026-10-20 原始新增三项测试在修复前全部失败：VO 缺字段一项，普通 `is_update/is_all` 编辑者保存超级目标角色两项；原红 TRX 保留。窄修后同一批离线 53/53、零跳过、警告即错误，含原角色工作台、共享核心目录证明/提取及七个新增正反例。实际调用原 Business、SqlRowStore 和安全协调器，数据传输为已有内存提供器；不是 MySQL、HTTP、RR、独立 Full 或原浏览器验收。
