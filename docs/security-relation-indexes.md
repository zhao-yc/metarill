# 三条安全表关联索引（元数据与物理同步均已完成）

已通过原索引工作台发布三条索引、六条字段引用，并在Server200的metarill_test完成三笔正式结构同步；真实ID与源头一致。当前结论及实际DDL证据见末节；下文保留此前只读基线和发布阶段记录。没有Seed、ORM自动同步或直接Mapper写入；旧元数据、既定37行图及原有物理索引保留，不操作角色/账号授权及既有服务。

## 实库只读基线

Server200的metarill_test已核数据库、hostname和UUID，MySQL8.4.11。SHOW INDEX证实yz_role、yz_role_permission、yz_admin_type只有PRIMARY(id)，yz_manager另有ux_manager_name(name)。按当前SqlRowStore完整投影、delete_no条件与真实过滤形状做EXPLAIN（没有ANALYZE，没有执行SELECT FOR UPDATE或开启事务）：

- 当前单ID样本角色及账号/后台主键查询使用PRIMARY，type=const，估算rows=1。
- permission按role IN并ORDER BY id使用PRIMARY全索引遍历，type=index，估算rows=96；同role等值查询type=ALL，估算rows=96。
- role和manager按后台检查引用均type=ALL，估算rows=47。

这些是特定时点统计和单个同后台角色样本的优化器估算，不是实际耗时、锁范围、死锁或大ID集合验收。不能由源码未声明索引推断现场一定扫描；本次先核了现场索引和计划。添加后的实际计划/并发/性能尚未验证。

## 固定源声明

三条均为非唯一、ASC二列索引：

| 表 | 名称 | 列顺序 | 稳定索引ID |
| --- | --- | --- | --- |
| yz_role_permission | yzp_idx_role_permission_role_active | yz_role_id, delete_no | 900235310779076608 |
| yz_role | yzp_idx_role_admin_active | yz_admin_type_id, delete_no | 900235316722405376 |
| yz_manager | yzp_idx_manager_admin_active | yz_admin_type_id, delete_no | 900235322581848064 |

每条索引的两个字段引用均固定同表已发布物理列，ID与identifiers清单一致；不附加id列，不改PRIMARY或ux_manager_name。MySQL二级索引携带主键；多role IN的全局id排序仍需添加后重新EXPLAIN，不能承诺消除排序或锁等待。

统一源头为scripts/build-platform-metadata.mjs，索引和索引列经相同构建器派生到platform.json，稳定ID进入identifiers.json。完整builder必须保留既有发布声明；先前闭包保护另有独立提交和CI回归。两轮生成字节稳定，files.json派生无变化。

## 离线预览与后续发布边界

离线测试用真实平台目录编译Schema，并在同一目标列结构上仅移除这三条索引作为模拟基线；Compare必须恰好三个addIndex，不能出现列更改、删除或唯一约束，已有名称但列顺序不一致须拒绝。该模拟不是完整现场结构反查，不会产生可在服务器执行的有效令牌或执行DDL。

本次使用IndexWorkbenchBusiness既有Snapshot/Save完整聚合生命周期保存三条声明：真实管理员、当前平台项目、每表完整现有托管索引列表与revision必须先核验并保留；不能空数组覆盖其它托管索引。工作台新项省略id，由服务器生成live索引/字段ID；仓库固定种子ID不能伪造成已存在索引ID传入。工作台正整数sortNo会规范化，需按列顺序核验，不能把元数据ID或位置数值混同物理索引定义。

本片先准备不含会话的可审请求；在fresh完整快照、revision和请求复核后才执行下节三笔元数据保存。后续应再从实际已发布目录走SchemaService.PreviewAsync；预览内容、实例/指纹与精确三条ADD INDEX需单独review。Apply是另一操作，MySQL DDL隐式提交，不能声称可事务回滚；本片没有Apply权限、令牌或执行记录。

## 原工作台发布与种子ID一致性

实际发布使用全新原HTTP登录会话、当前平台项目及每表完整Snapshot/revision，三表原托管集合均为空。请求省略新索引和字段ID，由服务器分配；三次独立聚合事务均HTTP成功，并回读确认新增恰好1+2行。所有旧配置记录全字段、既定37行图（物理37、墓碑4、活动33）和SHOW INDEX结构均未改变。最后注销本次自有会话并以实际工作台请求确认失效。没有执行物理同步。

真实ID回执如下，字段按列顺序列出：

| 索引 | 索引ID | 首字段引用ID | delete_no引用ID |
| --- | --- | --- | --- |
| yzp_idx_role_permission_role_active | 900235310779076608 | 900235310800048128 | 900235310821019648 |
| yzp_idx_role_admin_active | 900235316722405376 | 900235316734988288 | 900235316751765504 |
| yzp_idx_manager_admin_active | 900235322581848064 | 900235322598625280 | 900235322615402496 |

仅本次尚未公开的3个index稳定键和6个indexfield键映射到以上ID；已有1423个发布键值全部不改。MetadataRepository.SeedAsync按ID检查（含墓碑），不按索引名复用；不能让源使用临时ID而live使用另一套ID，否则下一次显式Seed会尝试新增同名配置。新增离线测试实际调用SeedAsync两轮：已有9ID（含墓碑）零INSERT/UPDATE/DELETE；缺失9ID（即使已有同名但不同ID）首轮仍尝试插入、次轮同ID不写。它使用内存ADO，不代表已在实库执行Seed，本次没有执行初始化或Seed。

原工作台将非唯一类型保存为NORMAL、序号归一为10/20；统一builder既有声明格式为INDEX、0/10。本次保持builder全局旧格式，三条源与live的ID、列ID、列顺序、非唯一及启用属性一致；不宣称原始JSON全字段相同。Seed按真实ID跳过，不会把归一化/audit值覆盖回源格式。正式Schema预览须从已发布目录读取；元数据保存不代表物理索引已建成。

私有工具首次执行在登录前因混合分隔符的本机父路径比较停止，Save发送数为0；保留原失败事实。两侧绝对路径规范化并使用Windows大小写比较后，离线验证合法混合路径、相邻同名前缀非法及其它父目录非法，重新复核后才执行原唯一prepare。三条实际Save均只发送一次，未知结果无自动重试。

## 2026-10-20 正式结构同步完成

在冻结9594d67、核实Server200的metarill_test实例后，三表逐一通过原database/preview取得实际计划，每表仅1条已审ADD INDEX。集成者审查安全投影SHA `8044ebff2a1b52e6552d10f9a9670ac47375ab8b90147f8c60d8d57a05aff6f4` 后，以新宿主fresh token重复预览并执行原database/apply，发送/成功/完整核验均为3/3/3。

每笔SHOW INDEX确认恰新增原二列ASC非唯一BTREE，PRIMARY及账号ux_manager_name保持；同表二次预览0差异，三条持久审计逐一匹配。所有旧元数据、9个真实ID、既定37行图和装载元数据指纹不变，自有会话注销后实际预览拒绝。没有Seed、其它DDL、重发或业务数据扫描；MySQL DDL隐式提交，不声称事务回滚。最终回执位于贡献clone `.local/security-index-schema-acceptance-v1/apply-20261020042708-40f8af298ec84ba3b9b9a7486b3892fa/final-result.json`，SHA `1074f37d162243bbc494081031db41db0e048516c792a8626785efc28293410e`。

物理索引存在及0差异不等于锁并发或性能达标。后续RR首次验收暴露观测账号缺少performance_schema权限，失败现场保留；专项仍待完整结论。