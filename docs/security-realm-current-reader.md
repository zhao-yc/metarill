# 身份后台的锁后当前读

本片将普通认证与安全写入的权限聚合抽为同一纯决策，并增加尚未注册的锁后当前读组件；还未接入项目/后台协调器、普通CRUD或工作台保存，不关闭角色Runtime Gap。

## 当前读边界

调用方必须先在同一DatabaseSession事务按稳定项目集合、后台集合预锁，提供真实当前行。Reader不自行补逆序聚合锁、不降级普通ReadAsync。工作区项目只参与兼容锁序，不授予身份或目标后台权限。

读取次序固定为当前Agent grant（若有）→当前sys_session→操作者→所属后台的有效角色ID集合→这些角色的授权→固定安全目录。所有查询使用FOR UPDATE和同一事务；空角色或页面集合不会退化成全表读取。Agent grant先于session与现有OAuth撤销/刷新相同，不与yz_role_permission混为一类。

会话种类、过期、账号绑定、Agent grant绑定、真实账号状态、后台/项目与当前请求身份均需一致。超级语义改变直接要求刷新请求，避免旧Actor.IsAdministrator继续流入文件或手写钩子；普通权限以锁后实际授权替换原快照。

元数据当前读只涉及发布项目内的固定表、同真实后台页面/菜单、页面所属按钮，复用普通MetadataRepository的物理别名和开关解释；不读取连接或字段明细。聚合只认可实际与已发布资源交集、同realm项目和后台，跨realm脏授权、软删页面或未发布页面不形成权限。页面/菜单各超过1000或按钮超过5000明确拒绝，不静默截断。没有性能实测，不宣称开销不变。

## 原规则与本次范围

原账号beforeManagerSave按省略状态合并后台/角色，仅校验角色属于最终后台，不对目标账号套用工作台的operator同后台条件。原角色beforeRoleSave/Delete则调用PermissionAccess，确有同登录后台及角色页对应动作/is_all规则。原后台写守卫防有引用时迁项目/删除，源页动作及既定workspace范围继续保留；不新增super授予门槛。

原Manager/Role Query局部无project条件，而当前C#有兼容project及既定workspace范围；原最终范围尚需共享QueryExecutor、数据权限拦截器和调用上下文全链证明，本片不改设备/项目矩阵，不称当前范围与原完全等价。兼容project列不能推导身份realm。

## 验证与待办

离线76项（当前读13、既有realm/工作台及有界集合邻近）全部通过、0skip、-warnaserror。使用实际认证、SqlRowStore和共享策略，固定内存传输只核对命令与决策：旧权限不沿用、所有安全查询FOR UPDATE、Agent锁顺序、会话/后台/超级变化、工作区与身份分离、跨后台/软删/未发布资源拒绝。未连接数据库，未证明Repeatable Read等锁后撤权或真实死锁行为。

下一片必须实现聚合预锁、最终merged归属/引用检查、BeforeWrite同事务、原普通安全页面权限门禁和工作台共用；raw yz_role_permission标准四写开关当前均为0，不能借通用Service绕过聚合工作台。真实并发、HTTP、原控件及最终集成验收后才能关闭Runtime Gap。
