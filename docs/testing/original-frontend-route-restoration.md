# 原页面固定请求盘点与三处接口恢复

日期：2026-10-31；C63起点 `c4cdee6`。只维护C#手写兼容入口、已有业务和仓储，不手改原页面、组件、样式或生成模板，不恢复旧双菜单表。

## 原始证据与边界

AST盘点实际读取pages/common/components的110个源码文件，以H5、App、微信三个条件编译集合分别解析，保留原Vue行号。得到330条固定请求声明（包含平台重复）、62组唯一HTTP方法/路径、277条无法静态确定的平台×调用记录，解析错误0。默认GET来自原共享request实际实现，不按接口用途猜方法；动态路径、方法或对象覆盖不猜测。该工具不执行源码，不声称覆盖动态调用、其他平台编译宏或请求的数据流。

实际平台Program的受控TestServer在MVC资源过滤器处停止，无数据库、真实会话或按钮执行。兼容兜底路由另走生产CompatibilityButtonPolicy及固定目录核对，不能以兜底路由匹配代替功能存在。初次探针4处404；修复后仍有下表最后一处404，完整探针暂存私有目录，未将失败改为预期成功或加入虚假空端点。

| 原固定入口 | 已落实行为 | 状态 |
| --- | --- | --- |
| POST /admin/custom/index/search | 当前配置管理员/项目、表与页面原字段；关键词SQL参数化，原OR/LIKE、ID倒序、SQL各50条；显示名只回读同项目活动目标，类型展示读取已发布常量 | 后端及离线回归完成 |
| GET /admin/sys/index/findNameIdYzManager | 原value/text与姓名—用户名，长ID字符串；真实账号页list权限及当前项目、活动行；只选id/real_name/name | 后端及离线回归完成 |
| POST /admin/auto/yz_role_permission/2063264600601714690/findNameIdConfigSecondMenu2063264600681406465 | 旧数组、page、selected地址委托当前ConfigMenu真实候选；保持来源页/目标页权限及范围、5000上限 | 兼容别名及离线回归完成 |
| POST /admin/custom/index/sync_runtime_all_projects | 用户已确认保留原按钮及一次确认，按当前设备逐项目接在线助手；无路径跳过，有路径但无绑定/授权明确失败，只把实际应用计成功 | 待实现，仍404；不能用已排队或当前项目代替全部 |

三个恢复端点不接受客户端表名、投影、SQL或项目授权。首页搜索和账号候选设置no-store。普通与含墓碑/锁定完整行读取未被投影读取替换。新增LIKE/有界读取方言集中在数据库适配器，首期只有MySQL实现；不据此宣布其他数据库可用。

## 实际验证

| 验证 | 结果 |
| --- | --- |
| AST解析器/条件编译护栏 | node --test tools/acceptance/original-frontend-routes.test.mjs：5通过，0失败/跳过；同项接入CI，尚不等于62路由全通过 |
| 元数据实际业务/仓储受控ADO | 严格494通过、25条件跳过；新增搜索25项及真实账号候选控制器5项，覆盖项目/墓碑/上限/参数化、目标名称隔离、发布标签、长ID、异常请求和权限 |
| 数据层 | 严格1222通过、24条件跳过，0失败 |
| 实际Program/API | 严格89通过、49条件跳过；新增旧/新候选响应与权限顺序、匿名登录超时及no-store合同，0失败 |
| 完整管理包 | 4通过，0失败/跳过；随包源码独立严格构建及原匿名边界，包含本轮依赖 |
| 前序提交CI | c4cdee6，GitHub run41069024416：前端124108276686、Ubuntu124108277007、Windows124108277172均completed/success；不替代本轮提交CI |

ADO夹具按真实SQL列和参数回传数据，但不是MySQL，不证明真实排序规则、锁、隔离或性能。原始搜索缺少显示字段、旧别名权限顺序断言错误及初期编译失败均保留私有日志；最终断言依据原合同与真实安全执行顺序修正，没有放宽产品授权。全固定路由红灯和动态调用待办保留。

私有证据位于E:\git\metarill\.local：original-frontend-routes-{first,second,third}.log、original-frontend-routes-inventory.json、home-search-{first,second}.log、original-restored-{metadata,api}-final.log、original-three-data.log、original-three-package-closed.log及各TRX。未提交日志/构建产物。未执行DDL/DML、远端账号/资源操作、页面写入或真实本机助手应用。

## 后续与升级

本轮三个入口无DDL、回填或页面再生成需求。平台及完整管理应用需配套更新Api、Runtime、Data，不能只复制控制器漏掉仓储方法。回退同样配套回退，不修改存量数据。原角色权限明细还存在旧菜单字段绑定/能力开关与新单表元数据的差异，需继续核对；本轮候选别名不代表整页写入恢复，更不能放开原已禁用的权限表普通CRUD。

批量功能按[已确认方案](../bulk-runtime-assistant-decision.md)继续；必须核验当前设备归属、逐项目配置/绑定/批准、原前后端路径开关、幂等与未知结果，汇总真实回执。原页面、真实MySQL/普通账号、浏览器、跨端与同条件性能另验。C61独占S3桶及C53/B102/C38新租约仍按各自范围处理，本轮没有复用旧授权。
