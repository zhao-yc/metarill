# 身份后台的锁后当前读

普通认证与安全写入共用纯权限决策；锁后当前读现已通过AddRealmWriteCoordination接入平台聚合协调、普通安全CRUD和工作台。独立生成Host完整Runtime发布Gap继续保留。下文76项是原切片历史，后续实证另记。

## 当前读边界

调用方必须先在同一DatabaseSession事务按稳定项目集合、后台集合预锁，提供真实当前行。Reader不自行补逆序聚合锁、不降级普通ReadAsync。工作区项目只参与兼容锁序，不授予身份或目标后台权限。

读取次序固定为当前Agent grant（若有）→当前sys_session→操作者→所属后台的有效角色ID集合→这些角色的授权→固定安全目录。所有查询使用FOR UPDATE和同一事务；空角色或页面集合不会退化成全表读取。Agent grant先于session与现有OAuth撤销/刷新相同，不与yz_role_permission混为一类。

会话种类、过期、账号绑定、Agent grant绑定、真实账号状态、后台/项目与当前请求身份均需一致。超级语义改变直接要求刷新请求，避免旧Actor.IsAdministrator继续流入文件或手写钩子；普通权限以锁后实际授权替换原快照。

元数据当前读只涉及发布项目内的固定表、同真实后台页面/菜单、页面所属按钮，复用普通MetadataRepository的物理别名和开关解释；不读取连接或字段明细。聚合只认可实际与已发布资源交集、同realm项目和后台，跨realm脏授权、软删页面或未发布页面不形成权限。页面/菜单各超过1000或按钮超过5000明确拒绝，不静默截断。没有性能实测，不宣称开销不变。

## 原规则与本次范围

原账号beforeManagerSave按省略状态合并后台/角色，仅校验角色属于最终后台，不对目标账号套用工作台的operator同后台条件。原角色beforeRoleSave/Delete则调用PermissionAccess，确有同登录后台及角色页对应动作/is_all规则。原后台写守卫防有引用时迁项目/删除，源页动作及既定workspace范围继续保留；不新增super授予门槛。

原Manager/Role Query局部无project条件，而当前C#有兼容project及既定workspace范围；原最终范围尚需共享QueryExecutor、数据权限拦截器和调用上下文全链证明，本片不改设备/项目矩阵，不称当前范围与原完全等价。兼容project列不能推导身份realm。

## 验证与待办

原切片离线76项（当前读13、既有realm/工作台及有界集合邻近）全部通过、0skip、-warnaserror。使用实际认证、SqlRowStore和共享策略，固定内存传输只核对命令与决策：旧权限不沿用、所有安全查询FOR UPDATE、Agent锁顺序、会话/后台/超级变化、工作区与身份分离、跨后台/软删/未发布资源拒绝。未连接数据库，未证明Repeatable Read等锁后撤权或真实死锁行为。

后续已实现平台预锁、最终merged归属/引用检查、同事务BeforeWrite、原安全页动作权和工作台共用，见[协调器](security-realm-write-coordinator.md)。raw yz_role_permission普通写仍关闭，不能旁路工作台。剩余独立宿主完整安全下发、账号原页浏览器完整写入及selection/export等门槛，不因平台通过而解除。

## 2026-10-20 当前读实库边界

冻结74889d5在Server-200独立临时实例RR3/3、零跳过。首项普通SELECT仍读旧快照而锁后读拒绝撤权写、目标全行不变；第二项后台变更冲突前不补新后台锁；第三项无关记录锁仍持有时两操作提交。21个实际参数计划正确，三组owned零残留；两个空库正式初始化到23/4表与3索引，实例已精确清理。共享采集未改，失败/授权撤销保留，见[RR专项](security-realm-rr-validation.md)。不据此证明全部配置入口、任意负载死锁或性能无退化。

后续冻结`9bc4693`非RR Agent完整161/161零跳过（显式排除RR三项），其中固定账号凭据真实HTTP/TestServer与MySQL 3/3：原完整表单新增哈希/启用映射、mask及空旧字段更新、省略角色、真实登录注销、非法输入全行不变和自有账号/session零残留均已核验。详见[验收台账后续批次](acceptance.md)。不将HTTP实库结果称为浏览器新增/改密，也不将分批RR并入该161。
