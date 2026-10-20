# 安全写入聚合协调器（平台已接线，独立实例RR专项已验）

平台宿主通过同一AddRealmWriteCoordination入口注册SecurityRealmMutationGuard→ConfigurationMutationGuard。三个安全Service手写partial、HTTP原页权与RolePermissionBusiness保存已接入实际协调器；角色Runtime Gap仍为独立生成Host保留：平台RR三场景和原页只读名称/掩码/启用已补验，但账号原页浏览器完整写入及独立Host完整协调器下发未完成。

## 原规则矩阵

- 账号标准写按真实源页动作和现有发布范围；目标账号不被额外要求与操作者同后台。以最终合并后台校验全部角色，包括省略角色但修改后台、null清空及[]清空；没有额外超级角色授予限制。
- 角色标准写除源页动作外，沿原PermissionWriteGuard→PermissionAccess要求目标角色与操作者同登录后台、本后台super或真实角色页相应new/update/delete/is_all权限。角色不直接迁后台，有有效账号或授权引用不删除。原源码未证明内置角色blanket删除禁令，平台手写partial已移除该额外禁令，仍校验实际pending及0/1类型；真正引用约束在同事务最终守卫检查。
- 后台标准写沿原源页动作及现有workspace范围，不额外要求操作者属于目标后台；有菜单、角色或账号引用不迁项目/删除。
- 原普通yz_role_permission页面新增、更新、删除、复制、导入均关闭；协调器也明确拒绝手工表级写，保存由聚合工作台接入共用准备入口，不能以通用Service旁路。

原证据为D:/svn/yzp只读：YzpPermissionWriteGuard.java:125–150、155–190、194–203，PermissionAccess.java:29–48，PermissionStore.java:50–65。Manager/Role局部Query与C#兼容project范围差异仍是独立全链待核，本片不改设备/项目矩阵、不称既定发布范围与原完全等价。

## 锁与当前读

首次准备的普通读取只发现候选锁集合，不能据此授权。集合含身份项目、兼容workspace项目、旧/新后台的真实项目及后台；先按数值项目顺序锁，再按数值后台顺序锁。后台当前归属若跑出已锁项目，或同事务后续操作需要新项目/后台，统一YZP_CONFLICT并回滚，不在首目标锁后补锁。

然后调用已审CurrentReader，严格Agent grant→session→operator→roles→role grants→当前目录。授权后再取目标记录锁，最后生成器传入原/最终窄范围、对象规则及关联规则完成后重新当前读并验证。工作区项目只是锁序兼容项，不是身份realm或目标授权；前后原值不能伪装。SQL ORDER BY不保证MySQL二级索引/filesort的物理锁序，协议依赖项目/后台聚合锁，后续真实双连接/三连接证据见末节。

准备过的作用域保持当前事务引用；RolePermissionBusiness可使用专用PrepareRolePermissionsAsync加入同协议，并取得锁后实际目标角色和同realm目录。Save已接入该入口，之后保留目标角色语义、revision比较和整份草稿校验；回执直接使用内部Snapshot，不对update-only账号追加query门槛。标准manager/role/admin页copy/import目前未开放，不能把准备阶段同realm锁集合当作未来任意批次范围收集已完成。

## 已接线约束和待验边界

- 实际IEnumerable<IRecordMutationGuard>中安全协调器先于旧ConfigurationMutationGuard；旧guard对后台写要求同一个准备对象已进入pending，反序会在旧项目SQL前拒绝，避免先workspace再更小principal项目反序。
- HTTP只恢复固定ID/name的manager/role/admin三个安全表真实同realm原页权；其他平台配置、global目录及Agent控制面不开放。手写partial不再增加platformsuper或内置删除门槛，但必须证明实际同Session/tx、同actor、同table、同操作及当前目标已准备；仅有guard实例、空guards、另一事务/目标均失败。新增无旧target仍要求create/copy操作与固定table，不能借一次update准备冒充新增。
- Business已共用协调器并保原revision/响应、update-only回执；初始目录坏/跨realm授权问题不冒称正常。本片不新增或放宽设备/workspace矩阵，也不证明所有config_*手写入口均有中途撤权一致性。
- 独占实库先SHOW INDEX/EXPLAIN，再RR等锁撤权、锁集变化回滚、实际DI生成Service和原HTTP/控件验收。只读元数据索引源不能证明现场索引、实际全扫或deadlock；初始接线片未改metadata、DDL或数据库；后续正式索引同步和独立RR证据见末节。
- Full与BackendOnly新包均只下发Core/Data/Files/Exports，生成器创建新的空手写partial，不复制平台本片三个Runtime依赖扩展。故BackendOnly仍可构建，但没有本平台A安全行为，不能称角色完整可下发；Full继续明确拒角色Runtime Gap。平台Api通过既有Runtime ProjectReference编译本片手写partial。

## 验证

106项离线（30守卫与76 reader/身份/工作台/集合邻近），实际产品守卫、SqlRowStore、AuthenticationService及共享策略执行，内存命令适配器无网络。覆盖普通账号跨目标后台关联super角色、按原role页权改super、3种新增、隐藏/省略/null/[]、跨后台和迁后台拒绝、授权/账号/菜单引用、最终锁集变化、两边界撤权/页面关闭、原范围伪造、rawPermission关闭。测试没有实际生成Mapper写入，也不证明数据库、浏览器、RR锁等待或整个A闭环。


## 最终接线的验证说明

新增离线接线回归通过真实AddGeneratedServices解析三个Service、真实共享DI顺序、编译进Service的手写partial正反、实际Service写入口在ORM前拒权，以及ASP.NET TestServer真实认证中间件/MVC请求的原页动作与下一请求撤权。partial反射调用只验证产品内部钩子，内存SqlRowStore只验证固定参数SQL/事务合同，均不是MySQL持久化或RR锁等待证据。

原AgentIntegration fixture不再手工传空guards，改用同一宿主注册入口；其旧“普通不能改super”暂态断言已按用户确认的原V8页权改成普通role页编辑正例、非法2回滚和陈旧revision不写授权。该冻结时实库用例尚待独占窗口，不计入离线结果；后续392项及RR专项分别见下节。

本次有意修改且须保留的手写文件为平台Services/YzManagerService.cs、YzRoleService.cs、YzAdminTypeService.cs；它们不属于.g.cs，也不能用旧partial字节不变断言覆盖。原接线片不手改标准产物，后续已由集成者完成两轮正规生成；三索引已按原工作台/Schema计划执行。RR使用实际参数EXPLAIN，不以离线SQL或ORDER BY替代锁证明。


最终离线验证共128个唯一case，均0skip：Agent身份/当前读/聚合/工作台/新接线123、Metadata原配置guard及候选拒权4、既有隐藏角色真实生成编译与Runtime Gap门禁1。Api/Agent/Metadata测试程序集、实际CLI及本次新平台BackendOnly包宿主均已编译，警告和错误均为0。平台完整包实际先被既有Custom组件门禁拒绝；不能将其错误当作角色门禁已执行，角色门禁由独立的隐藏角色生成case和生成能力清单证明。


## 集成者实库回归（2026-10-20）

冻结9909c21在隔离配置下完整API71、Agent142、Metadata150、Compatibility29，共392项通过，零失败零跳过；包含离线case，不等同392个锁并发案例。新接线的普通页权与工作台已有实库回归，该批未含RR专项及后续原页只读浏览器，不将392项当成锁案例；后续证据见末节。两轮正规生成稳定；三份有意修改的手写partial进入新24文件基线，其余21文件保持旧摘要。

## 2026-10-20 独立实例RR与当前发布边界

冻结74889d5归档隔离构建后，在Server-200独立临时实例实际RR3/3、零跳过：旧快照撤权后拒写且目标不变；后台迁移后YZP_CONFLICT，在回滚前实测A锁存在/B锁不存在；无关realm锁仍持有时删除和工作台提交。21个实际参数计划正确，三组owned行/会话/授权清理零残留；两个空库正式Bootstrap到23/4表及3索引，RR前后结构同字节，实例/卷/隧道及运行秘密已精确清理，见[RR专项](security-realm-rr-validation.md)。共享临时17列授权撤销后应用三检查1142，未改共享采集开关。

安全核心13/API71/Metadata152/Compatibility29、定向Generation69通过为各自批次，不能把旧完整Generation596通过/14失败/2专用跳过改成全绿。f3740cd原账号/角色名称、掩码和启用只读浏览器新增/保存0，不是账号浏览器完整写入。Metarill.Security核心提取不等于协调器/Runtime适配/工作台已下发；独立Host安全Runtime、selection/export、5个Custom页面、其余配置入口、性能及跨端设备继续受独立门槛约束。

后续冻结`9bc4693`非RR Agent完整161/161零跳过（显式排除RR三项），其中固定账号凭据真实HTTP/TestServer与MySQL 3/3：原完整表单新增哈希/启用映射、mask及空旧字段更新、省略角色、真实登录注销、非法输入全行不变和自有账号/session零残留均已核验。详见[验收台账后续批次](acceptance.md)。不将HTTP实库结果称为浏览器新增/改密，也不将分批RR并入该161。
