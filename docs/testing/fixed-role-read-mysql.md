# 固定角色读取用途的MySQL专项验收

本专项只新增测试源码；不修改产品、canonical元数据、账号导出/复制/导入开关，不发布页面或执行DDL。2026-10-20由主协调者在固定源上独占执行，三项真实MySQL用例及两项离线用例共5/5通过，零失败、零跳过。验收范围是原HTTP协议的TestServer、真实MySQL读取及真实XLSX回读，不是浏览器验收。

## 用例

- `Manager_selection_has_real_scopes_names_and_source_query_authorization`：真实普通账号只获来源账号页query，target角色页无query；type=2回执保持首次选择顺序和长ID，角色数组内顺序/重复、缺失/外项目/角色软删/后台软删的null位置不变；账号源行外项目/软删整体拒绝，凭据不进SQL/VO，query不能代替export。
- `Role_selection_projects_only_current_project_active_admin_names`：真实来源角色页query不借目标后台query；同项目有效后台名为string，外项目、软删、缺失目标为null；错误来源页和已删源角色拒绝。
- `Export_only_http_keeps_original_json_and_fixed_admin_name_in_real_xlsx`：两个来源页仅export，无query/new/update/is_all。原 `initExport` Controller→生成Service→实际MySQL→真实Excel导出器，ClosedXML回读角色原IDJSON/空单元格/`[]`和后台名string/null；selection不能借export权限。

## 夹具和边界

私有完整发布副本只在本机临时目录打开两页type=2/export，并由同副本生成固定三表读取程序集；真实认证器会根据发布开关过滤动作，因此认证Registry也使用这份私有发布副本。既有数据库 `config_page`、按钮和默认账号均不变；不调用MetadataRepository.Seed、不安装结构、不向平台写配置。普通账号使用原 `AuthenticationService`、`PlatformSecuritySessionPolicy`、`AgentAuthenticationMiddleware` 和 `HttpRuntimeAuthorization`，无伪Actor/permission集合。TestServer只在实库Fact运行时创建，不监听外网。只注册私有生成Controller，测试HTTP帮助器仅允许两个固定来源页的selection/selected与initExport；导出必须带非空的已登记SelectedIds。

实际写入只为私有测试数据，使用SqlRowStore在既有安全表插入预登记的随机账号、后台、角色、权限及真实登录会话；故意的跨项目/缺失关系/墓碑用于读取阴性，不宣称这些种子经过账号写业务验证。所有名称包含nonce，先查id/name含墓碑不存在，再写意图收据并刷盘。操作者ID在任何种子写前预查；会话ID由只允许一次分配的生成器预登记，真实登录后核account/project/kind/token摘要，摘要不入收据。

写前pin `deve-server.yinzon.com:3306` / `metarill_test` / 由显式私有 `METARILL_ROLE_READ_INSTANCE_PIN` JSON文件提供的expectedHostname和expectedServerUuid，拒其他实例；缺文件/重复键/未知字段/空值/非法格式均在连接和夹具写入前拒绝，源码/文档/错误/收据不公开真实实例标识；只读两个既有项目及平台后台作归属锚点，不创建/修改它们。样本所有权及全部声明FK反复核id/name/insert_manager_id/project；只记录全行摘要，不向断言或报告传账号/会话行集合。SQL观测仅使用本Session的ORM事件，不开performance_schema、不申请权限。

成功时先证明读取前后全自有图不变，再由产品Logout注销自己的唯一会话。清理在同事务按ID升序锁两个既有有效项目，先按ID升序锁自有后台，再以固定表名/ID顺序锁其它自有行并复核完整指纹，随后对索引FK以FOR UPDATE当前读拒绝未知引用（包括后台的config_menu引用，只读不写）。JSON角色引用只读取ID、不加FOR UPDATE；在前置仅有项目/自有行/FK的current锁读之后，第一次普通SELECT建立锁后的新RR快照，正式同项目写入口此时受项目聚合锁串行，避免对共享manager表加全表锁。不改变隔离级别，也不承诺能协调未知直接SQL绕过项目锁的写入，发现未知引用或第三种内容即保留现场。只有全部检查通过，才按会话→权限→账号→角色→后台精确删除，核id/name/creator/project（会话核account/kind/token摘要）。最终逐id及name包括墓碑证明零残留，收据只含非敏感ID/名称/归属FK/角色ID/全行指纹摘要/阶段/计数，不序列化Expected密码或token_hash。失败或未知结果只封存现场，不自动清理、重试或认领外部引用；不按项目/前缀泛删，不触默认数据或37图。

## 执行状态

`Private_role_read_model_really_compiles_without_mutating_canonical` 是离线生成Fact：不读私有配置、不建数据库连接或TestServer，真实生成并编译私有三表程序集，检查读取方法和来源权限接线，核canonical文件SHA及原账号开关未变。另有纯离线pin解析Fact覆盖合成正例与缺失/重复/未知/非法字段；三项真实用例使用 `AgentIntegrationFact`，缺少既有私有配置与平台元数据时明确skip，不把skip计为真实通过。

2026-10-20验收的测试源为 `41005f2d71f7e410f199d79a7929983285492e30`，根集成提交为 `4564b6893bdc7454a74b592eb9d9bcf00b96b33f`。主协调者一次运行本类5项：2项离线、3项真实TestServer/MySQL，全部通过，零失败、零跳过；本次文档复核只读取已保存证据，没有重跑测试、数据库请求或服务。

三个实库场景分别创建独立TestServer和自有随机样本，执行原Controller→生成Service→真实MySQL路径：

| 场景 | 本次实际通过的关键断言 | 生成读取SQL计数 |
| --- | --- | --- |
| 账号选择回读 | 真实普通账号只有来源query，目标角色页无query；所选行按首次顺序去重，长ID不丢精度，角色数组顺序/重复位置保留，缺失、跨项目及双软删名称为null；凭据不进入SQL/VO，无权请求在源查询前拒绝 | 成功路径2条：来源投影1条、绑定参数的有界名称读取1条 |
| 角色选择回读 | 真实来源query不借后台页query；同项目活动后台名为string，外项目/软删/缺失为null，错误来源权限与已删源角色拒绝 | 成功路径2条：来源投影1条、绑定参数的后台名称读取1条 |
| 仅导出权限 | 两来源页仅export，不能借该权限selection；ClosedXML实际回读XLSX，账号角色列保持原IDJSON文本、长ID/数组顺序/重复及null/`[]`，角色后台列为名称或空单元格，不改成角色名称导出 | 账号导出1条；角色导出2条，其中1条读取后台名称 |

上述计数来自生成读取Session的ORM事件，逐请求清空并断言，不能外推成认证、样本建立、清理或全部框架请求的总SQL成本。阴性用例还实际断言越界源行只查询来源一次、无权请求不发生成读取SQL。本专项不是吞吐、并发锁或RR快照验收。

## 原始证据与精确清理

原始证据保留在根工作区的私有 `.local`，不进入Git；以下只记录可复核路径及摘要，不公开实例hostname、UUID或凭据。

- `.local/fixed-role-read-uses-mysql-integrated.trx` / 同名 `.log`：5执行、5通过、0失败、0跳过。TRX SHA256：`a5e84f0f72ad7a412d22ab9a7b819151f29adaea09c7af888a5326f2b5eabd4a`。
- `.local/fixed-role-read-uses-preflight-20261020/server-pin.json` / `.log`：固定Server-200实例及 `metarill_test`；三张performance_schema观测表的只读权限检查均为MySQL 1142，无GRANT、无新增数据库账号、无请求全局PROCESS。用例只用自身ORM事件，不依赖或增授观测权限。JSON SHA256：`94f6ce9a48d7ab216d88f89e0002109b327ca141dd25e3f8a09c7fc98a06eef1`。
- `.local/fixed-role-read-uses-verification-20261020/verification.json`：主协调者在测试后另做只读实例pin与精确ID/名称回查，三份完成收据共52行含墓碑全部零残留，无数据库写入或GRANT。SHA256：`88d2a2f6c6465bef8c7c1c1230628798189f98a58531df8adaff3bc75f5bcf38`。

三份原始完成收据均为 `stage=completed`、`completed=true`、`failed=false`，其cleaned计数与登记行数一致，且SHA与独立回查所登记摘要相同：

| 私有收据（相对于 `.local/fixed-role-read-mysql`） | 自有行/已清理 | SHA256 |
| --- | --- | --- |
| `2fb08983709e4199/receipt-completed-590c48286aac4dc8843da1004bba3505.json` | 18/18 | `48dda8b5e0ebf3a8e0b9fc23c450590a903ac1404572ab9241a1863ab99e1bd7` |
| `338ca8bb22d94291/receipt-completed-cc8ce24345e8461da2290ba1fba8ea8f.json` | 17/17 | `90b784216d09afd035ce6e920219dafbb1223e1fa495ff1b94ae73d6c3e848fa` |
| `f4345c4ca6bd4e4c/receipt-completed-b3c82a4051744821bc6192ad89a3f8c3.json` | 17/17 | `a7d200abff23116a8d07f3175d83c8c503673f26c6e5412de7350ce17b92b200` |

独立回查工具v1曾将收据字符串ID按JSON number读取而失败；原源文件、失败日志保留。v2按规范整数字符串解析后只读核验通过，保留 `verification-v1.log`、`verification-v2.log` 及最终JSON；没有重跑测试、改数据库或把v1失败改写为成功。

平台canonical开关及共享配置元数据未因私有测试模型而改变。本结果只覆盖上述固定读取用途；Runtime A下发、五个Custom前端依赖、新独立Host及原浏览器验收不能因此宣布完成或关闭相关门禁。

## 与同期 CI 的区别

head `c141bd3f196d7fd5abc34c89e9871d6dee537d94` 的 PR/push 六作业最终均 success，四后端日志每作业九个工程；Generation Linux 为625通过/12跳过、Windows为626通过/11跳过，各637。私有 `.local/ci-c141-final/receipt.json` SHA256：`fe6c6c96519e408d65fc4bcbb0708c56ed7814cc025f7ae02316f4fbb30bec7d`，完整作业与工程计数见 [验收台账](../acceptance.md)。该 CI 不持有私有DB配置，也不包含随后41005f2→4564b68新增的本专项五项；它与上文5/5独占实库批次分开记录，不将skip计为真实数据库通过。
