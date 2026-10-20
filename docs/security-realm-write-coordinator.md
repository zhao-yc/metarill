# 安全写入聚合协调器（待接线）

本片增加 SecurityRealmMutationGuard 两个源 partial 及实际守卫离线合同。还未注册DI、改Controller/HttpRuntimeAuthorization、接入RolePermissionBusiness或解除手写Service临时门槛；应用当前行为不因本片改变，角色Runtime Gap保留。

## 原规则矩阵

- 账号标准写按真实源页动作和现有发布范围；目标账号不被额外要求与操作者同后台。以最终合并后台校验全部角色，包括省略角色但修改后台、null清空及[]清空；没有额外超级角色授予限制。
- 角色标准写除源页动作外，沿原PermissionWriteGuard→PermissionAccess要求目标角色与操作者同登录后台、本后台super或真实角色页相应new/update/delete/is_all权限。角色不直接迁后台，有有效账号或授权引用不删除。原源码未证明内置角色blanket删除禁令，当前C# partial尚未修改，下片按原链移除该额外禁令。
- 后台标准写沿原源页动作及现有workspace范围，不额外要求操作者属于目标后台；有菜单、角色或账号引用不迁项目/删除。
- 原普通yz_role_permission页面新增、更新、删除、复制、导入均关闭；协调器也明确拒绝手工表级写，保存由聚合工作台接入共用准备入口，不能以通用Service旁路。

原证据为D:/svn/yzp只读：YzpPermissionWriteGuard.java:125–150、155–190、194–203，PermissionAccess.java:29–48，PermissionStore.java:50–65。Manager/Role局部Query与C#兼容project范围差异仍是独立全链待核，本片不改设备/项目矩阵、不称既定发布范围与原完全等价。

## 锁与当前读

首次准备的普通读取只发现候选锁集合，不能据此授权。集合含身份项目、兼容workspace项目、旧/新后台的真实项目及后台；先按数值项目顺序锁，再按数值后台顺序锁。后台当前归属若跑出已锁项目，或同事务后续操作需要新项目/后台，统一YZP_CONFLICT并回滚，不在首目标锁后补锁。

然后调用已审CurrentReader，严格Agent grant→session→operator→roles→role grants→当前目录。授权后再取目标记录锁，最后生成器传入原/最终窄范围、对象规则及关联规则完成后重新当前读并验证。工作区项目只是锁序兼容项，不是身份realm或目标授权；前后原值不能伪装。SQL ORDER BY不保证MySQL二级索引/filesort的物理锁序，协议依赖项目/后台聚合锁及后续真实双连接验收。

准备过的作用域保持当前事务引用；RolePermissionBusiness可使用专用PrepareRolePermissionsAsync加入同协议，并取得锁后实际目标角色和同realm目录。该入口当前尚未接入Business。标准manager/role/admin页copy/import目前未开放，不能把准备阶段同realm锁集合当作未来任意批次范围收集已完成。

## 下片接线门槛

- 实际IEnumerable<IRecordMutationGuard>中安全协调器必须先于旧ConfigurationMutationGuard，旧guard对后台写复用已持有安全锁，避免先workspace再更小principal项目反序。
- 只恢复3安全表真实同realm原页权；其他平台配置、global目录及Agent控制面不开放。移除Role partial临时platformsuper/内置删除blanket时，保留0/1类型、原后台归属和引用规则。
- Business共用协调器后必须保原revision/原响应，update-only不追加query门槛；初始目录坏/跨realm授权问题不冒称正常。
- 独占实库先SHOW INDEX/EXPLAIN，再RR等锁撤权、锁集变化回滚、实际DI生成Service和原HTTP/控件验收。只读元数据索引源不能证明现场索引、实际全扫或deadlock；本片未改metadata、DDL或数据库。
- Full独立宿主当前未下发Metarill.Runtime，本平台协调器验收不代表所有Standalone具备同授权；不得直接全局删除角色门禁。

## 验证

106项离线（30守卫与76 reader/身份/工作台/集合邻近），实际产品守卫、SqlRowStore、AuthenticationService及共享策略执行，内存命令适配器无网络。覆盖普通账号跨目标后台关联super角色、按原role页权改super、3种新增、隐藏/省略/null/[]、跨后台和迁后台拒绝、授权/账号/菜单引用、最终锁集变化、两边界撤权/页面关闭、原范围伪造、rawPermission关闭。测试没有实际生成Mapper写入，也不证明数据库、浏览器、RR锁等待或整个A闭环。
