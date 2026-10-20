# 固定角色读取用途的MySQL专项准备

本片只新增测试源码；不修改产品、canonical元数据、账号导出/复制/导入开关，不发布页面或执行DDL。三项实库用例须由主协调者审核后独占运行；源代码准备和离线编译不能算真实SQL/XLSX数据库验收通过。

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

本片实库三项尚未执行；后续由主协调者记录固定SHA、TRX、实例pin、实际SQL、XLSX断言和精确零残留收据。Runtime A、独立Host、五Custom依赖和原浏览器验收不由本测试准备解除。
