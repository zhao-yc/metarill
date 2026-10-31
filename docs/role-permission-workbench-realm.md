# 角色工作台身份后台与原V8入口

本切片保留原URL、JSON、组件、交互和表结构，修复工作台身份范围、目录及revision，并恢复原V8的工作台动作授权。它不接角色复选控件，不关闭角色Gap，不开放其它平台配置页或Agent审批。

## 原行为证据与目标范围

旧YZP的 `backend/yzp/src/main/java/com/yinzon/yzp/runtime/permission/management/YzpPermissionManagementBusiness.java` 中，roles（29–35行）取真实manager后台并要求`is_query`；snapshot（39–44行）要求目标真实role后台的查询权；role（137–142行）明确拒绝目标role与manager不同后台。目录（106–108行）由目标role后台推导，不由客户端workspace推导。

同目录 `YzpPermissionAccess.java` 的29–48行先检查相同真实后台，再允许本后台super；普通账号须有该后台实际角色配置页的对应动作或`is_all`。原Workbench不要求操作者是平台全局超级管理员，也没有平台super选择其他workspace就能管理其他principal后台的例外。

因此操作人的身份后台与目标角色概念分别验证：实际账号→实际后台→实际项目建立principal；请求仅定位真实role，role→实际admin→project推导target并核对同principal。账号/角色兼容project列、客户端selectedProject及formParameters不作为授权。目录只包含target后台的菜单、页面和其按钮，同项目另一后台也不参与。

## 当前工作台入口

- `roles`和`snapshot`要求原`is_query`，`save`要求原`is_update`；同后台super按原规则绕过角色页资源授予。
- 普通账号只有已发布、同realm、实际表为`yz_role`的角色页动作或`is_all`才能授权。其它页面、跨后台页、未发布页及内部污染的permission字符串均不能冒充角色管理权。
- 工作台已移除迁移期间的`RequireAdministrator`门槛。它不修改全局`HttpRuntimeAuthorization`或Agent控制面，业务super的`Actor.IsAdministrator`仍为false。
- 保存回执由内部snapshot构造，不在已经提交后另调用查询鉴权；只有修改权而无查询权的合法原API不会产生“写成功却返回无权”。
- 原请求仍是GET roles、POST snapshot `{roleId}`、POST save `{roleId,revision,grants}`，长ID和RESOURCE响应结构不变。超级目标角色的资源授权只读规则沿用现有实现，不改变绑定super或role.isSuper的最终用户规则。

保存仍使用原事务和项目锁，新增授权行归真实target项目。revision包括角色ID/admin/super/scope、实际角色表归属及同后台菜单/页面/按钮目录与授权源行；变化使旧草稿冲突，摘要不输出SQL、连接或密码。

## 验证与未完成边界

离线测试执行真实AuthenticationService、RolePermissionBusiness、MetadataRepository和SqlRowStore，固定内存传输不联网。原query正例、普通update/is_all、业务同后台super，以及认证后实际账号后台变化五个反例在修复前全部失败。新增普通无权、其它页面/跨realm污染、修改无查询、工作区不换身份等正反验证；没有把普通夹具改成super以绕过权限。

旧API实库fixture原先由平台身份在Demo workspace管理Demo后台role，与原V8硬性同后台规则矛盾。现在为授予/撤销方建立真实Demo后台super账号，保留原“授读权→普通能读不能写→菜单不授读→撤权”完整断言，另明确平台super切Demo仍拒管理Demo角色。只清理本测试唯一ID归属的账号、角色、授权和会话；实库执行须等待Server-200独占窗口，不能将源码修正或离线测试冒充HTTP/MySQL通过。

尚未完成项目→后台预锁协调器及锁后session/operator/role/permission/目录current-read。现普通SELECT在MySQL RR事务中仍可能读旧快照，不能据此声称中途撤权或真实并发已安全。角色/账号标准CRUD的迁移暂态门禁仍待B按用户确认的原页权限恢复，不是最终增加的超级门槛。其它config写和工作台的中途撤权亦未在本片证明；工作区设备矩阵不在范围。本轮无schema变化、无实库/HBuilderX/浏览器验收，也不声称性能不变。
