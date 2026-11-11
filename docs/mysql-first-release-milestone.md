# MySQL 首版交付节点

确认日期：2026-11-02。用户将当前目标调整为“先把 MySQL 首版作为明确交付节点”。本文件定义当前交付范围。2026-11-06 确认、2026-11-07 再次确认首版交付 Web 管理端及 Web 业务生成，小程序、Android 和 iOS 转入后续兼容性验收。后续 PostgreSQL、SQL Server、MariaDB、Oracle、SQLite 适配保留在路线图，不是本节点完成条件。此前记录中包含“继续其它数据库”的全项目目标，按本次明确分阶段决定解释，不改写历史证据。

## 本节点目标

完成 E:\git\metarill 的 C#／ASP.NET Core MySQL 首版：保留 YZP 元数据、自举和生成流程，完整沿用原 uni-app 页面、样式、控件、交互及生成模板，由 C# 后端适配原接口；完成首期功能与真实验收，形成可审查、可复现、可交付的开源版本。

## 完成条件

| 范围 | 必须交付与核对的证据 | 当前入口 |
| --- | --- | --- |
| 平台及独立应用 | MySQL 平台库和业务库分别配置；空库显式初始化、重复保护、原登录到配置、结构差异预览／执行／核验、生成到业务 CRUD；普通启动不自动 DDL | A各项及原验收台账（含A35固定三表数值正式通过，原A30失败留证） |
| 原界面及生成 | 原页面、PC／移动布局、控件、交互和模板对照；接口、R／分页／字符串ID／字段存在性保持；重复生成稳定且保护 partial | A03／A05、V01—V03、前端恢复台账 |
| 权限及复杂业务 | 普通真实身份下的页面／按钮／菜单、数据范围、关系／非冗余读取、多值／级联／复杂联接、改名传播／维护、文件、导入导出及失败整批回滚；平台／业务库组合与并发按首期矩阵逐项完成 | B／C各项与V04；B121固定内部六模型及B123真正公开同包六模型正式通过，旧B120失败留证，其余未验范围继续 |
| 工具及宿主 | 原在线助手按钮、生成更新、CLI、MCP、Agent、正式认证和独立运行时的完整已定功能接线及真实验收 | C各项、独立应用与助手文档 |
| Web 验收 | 原 Web 管理端及生成的 Web 业务应用须在实际浏览器中通过首版业务验收，分别核对桌面与移动视口。 | V01—V03 及前端专项 |
| 性能 | Server-200上Java／C#同数据、SQL、索引、并发与资源对照；稳定退化阻断交付，不以单项微基准或CI代替 | V08、V17及后续成对性能专项 |
| 交付收口 | 最终固定源码／包／元数据／数据库／原界面一致；规定回归及该SHA的CI通过；限制、配置、升级与恢复说明齐全；全部必要资源清理核实 | V09—V11、实施进度表与各专项回执 |

上述编号是追踪入口，不把所有历史行都机械视作独立发布门槛。重复、历史失败、已替代方案分别留证；V11应按本文件及当前进度表核对明确需求的完整证据，存在缺失或未执行不得标完成。

## 范围与授权

- 微信小程序、Android 和 iOS 从 MySQL 首版发布门槛移出，在 [后续兼容性验收](project-progress.md#五后续跨端兼容性验收) 独立跟踪。保留已取得的构建、模拟器与设备证据。桌面和移动 Web 浏览器仍按上表验收。
- 当前只宣布通过完整验收的MySQL版本／能力范围；驱动可连接、离线通过或绿色CI不等于其它数据库正式支持。
- PostgreSQL、SQL Server、MariaDB、Oracle、SQLite保留数据库适配边界，待本节点交付后再安排后续阶段；本节点完成不要求实现D01—D05，也不自动启动这些适配。
- 用户此前决定Tiny Cloud key先留空，继续列为待配置项，不改为内置editor或默认加载成功；需要配置的真实验收据实列出，未经用户确认不删除首期要求。
- 保持既定`codex/initial-platform`开发分支、可审查提交及推送，不擅自合并main或部署生产。MySQL首版验收通过与生产发布是两个动作，前者不自动授权后者。
- 首版按已确认的新部署、固定源码及发行包交付范围验收。实际上线域名和公开证书链列入上线前环境检查；旧版数据／源码迁移、跨版本恢复及新增存储供应商适配列入各自后续事项。安装手册保留这些操作和证据要求。
- Agent自行创建且可核实归属的合成测试库、表、账号、容器、网络及临时文件，按用户持续授权自主创建、验收、重试及精确清理，无须逐轮请求审批。每轮核对准确范围和实例，重试前确认前轮回收，结束后独立核零并留证；现有业务和生产资源不在该范围。

## 当前状态

当前冻结候选为 `e682cd12`。平台和独立 Full 的两份 Linux x64 ZIP 已独立复建，并通过分库、共库和独立 Full 三种安装与重启。原配置表下拉已核对桌面和移动浏览器的排序、搜索及注销。[V1125／V1126 双包与三拓扑](testing/storage-mode-linux-release-v1009-v1010.md#v1125-与-v1126-排序候选独立双包及三拓扑) · [V1155 原配置表下拉](relation-options.md#v1155-当前原配置表下拉与注销)

MySQL 首版总验收仍在进行。当前候选 GitHub CI 15 项、Windows 固定源码 30 组已通过，Windows 完整生成器六组及独立 Linux 完整回归正在执行。九类性能的最新同条件正式结果达标，逐次修复初测退化留证。真实 Tiny Cloud 编辑待有效配置。[当前 CI](testing/ci-current-contracts.md#v1138-当前候选-github-ci) · [Windows 固定回归](testing/current-windows-regression-v946-v947.md#v1131-当前候选-windows-固定回归) · [独立 Linux 回归](testing/current-linux-generation-batches-v945.md#v1141-当前候选独立-linux-命令文件复验) · [实施进度表](project-progress.md)

此前候选 abbe89fb 已独立复建并通过三拓扑安装。两份 Linux x64 ZIP 逐字一致，实际分库、共库及独立 Full 通过。[V1028 当前候选三拓扑](testing/storage-mode-linux-release-v1009-v1010.md#v1028-导航修复候选三拓扑实际安装)。当前源码的原菜单双视口验收另见 V1026。

前一已通过三拓扑安装的候选为 `310a0ab3`。两份 Linux x64 ZIP 独立复建逐字一致。[V1009／V1010 发行候选](testing/storage-mode-linux-release-v1009-v1010.md)。同提交的整体回归继续核对。测试夹具提交 `0d8b64cc` 只延长结构反查测试的冷还原和构建等待，产品源码和实际命令期限沿用固定发行版本。该测试提交的 [GitHub CI](https://github.com/zhao-yc/metarill/actions/runs/42861403117) 全部 15 作业通过。后续测试提交 250dcf18 的 [GitHub CI](https://github.com/zhao-yc/metarill/actions/runs/42915313956) 继续核验。

固定候选 86775982 已通过两包独立复建、三拓扑安装、双视口 Web 及 Windows 固定源码回归。86775982 完成 45,756 个正式请求及 66 个配对轮次，九类均达标。见 [V819 九类同条件性能](testing/append-layout-mysql-performance-v819.md)。 固定产品的 [GitHub CI](https://github.com/zhao-yc/metarill/actions/runs/42637193256) 15 项通过，最终开发提交继续核验。见 [V817—V820 固定发行包验收](testing/append-layout-release-verification-v817-v820.md)。

已通过完整性能矩阵的早期基线为 `4ef3ca76`。候选 `a88bbc89` 完成三拓扑安装和九类性能测量，其中同父明细导入退化。历史候选 `2b654bcd` 完成两包重建及三拓扑安装，同父明细四并发仍阻断签收，九类矩阵复验继续。[V793／V795／V796 同一候选复验](testing/exact-lock-release-v793-v795.md)。

| 范围 | 当前候选证据 | 状态 |
| --- | --- | --- |
| 开源发行包 | e682cd12 的平台和独立 Full 双包独立复建一致。[V1125／V1126 排序候选](testing/storage-mode-linux-release-v1009-v1010.md#v1125-与-v1126-排序候选独立双包及三拓扑)。 abbe89fb 两份 Linux x64 ZIP 通过独立复建和实际安装。[V1028 当前候选三拓扑](testing/storage-mode-linux-release-v1009-v1010.md#v1028-导航修复候选三拓扑实际安装)。 310a0ab3 两份 Linux x64 ZIP 通过独立复建及实际安装。[V1009／V1010 发行候选](testing/storage-mode-linux-release-v1009-v1010.md)。 0b65780c 的平台与独立 Full Linux x64 ZIP 独立复建逐字一致，程序集提交及 103 份宿主和元数据已核对。见 [V943 当前发行包](testing/text-projection-linux-release-v943.md)。 1926a702 的平台和独立 Full Linux x64 ZIP 各复建一次且逐字一致，103 份安装载荷和 18 个程序集提交标记已核对。见 [V888 当前 Linux 包](testing/current-linux-release-v888.md)。 先前 `ffcd80d3` 的平台和独立 Full Linux x64 ZIP 独立复建逐字一致，103 份安装载荷固定。见 [V874 当前 Linux 包和三拓扑安装](testing/current-linux-release-v874.md)。 86775982 两份 Linux x64 ZIP 已独立复建，与安装包逐字节一致，103 份宿主及元数据核验通过。见 [V817—V820 固定发行包验收](testing/append-layout-release-verification-v817-v820.md)。 平台与独立 Full 的两份 Linux x64 ZIP 独立重建逐字一致，核对程序集来源和许可清单。[V750](testing/leaf-public-release-build-v750.md)。修复后 a88bbc89 两份发行包已独立重建，与正式安装包逐字一致。[V787 Linux 发行包复现](testing/patched-linux-release-rebuild-v787.md)。 最新 2b654bcd 两包独立重建通过。见 [V793／V795／V796 同一候选复验](testing/exact-lock-release-v793-v795.md)。 新 bd70b0e7 两份 Linux ZIP 已构建并逐份核对。见 [V806／V807 固定发行包](testing/parent-plan-linux-install-v806-v807.md)。 | 通过 |
| 空库与生成 | e682cd12 的有／无项目列反查导入、原配置、生成及独立 Production 已复验。[V1159 当前反查生成](schema-import-draft.md#v1159-当前候选原配置和反查生成)、[V1160 当前反查包独立运行](testing/imported-application-production-v293.md#v1160-当前反查完整包独立运行)。 e682cd12 的平台与示例目录末条写入故障、逐目录回滚和安装恢复已通过。[V1158 当前发行包种子回滚](testing/platform-seed-rollback-native.md#v1158-当前发行包两类目录末条种子回滚)。 e682cd12 平台及独立 Full 在严格 MySQL TLS 下完成证书拒绝、三库安装和原业务接口。[V1150 当前候选严格 MySQL TLS](testing/mysql-strict-tls-web-v565.md#v1150-当前发行候选严格-mysql-tls)。 e682cd12 的原配置、实际下载、空库独立安装和原 CRUD 已通过。[V1148 当前生成下载与独立安装](testing/current-generated-delivery-v917.md#v1148-当前发行包配置下载及独立安装)。 e682cd12 的分库、共库、独立 Full 已通过空库安装及移除引导密码后重启。原会话和原密码重登均通过。[V1126 当前三种部署实测](testing/storage-mode-linux-release-v1009-v1010.md#v1125-与-v1126-排序候选独立双包及三拓扑)。 abbe89fb 平台及独立 Full 在严格 MySQL TLS 下完成证书拒绝、三库安装和原业务接口。[V1080 当前候选严格 MySQL TLS](testing/mysql-strict-tls-web-v565.md#v1080-当前发行候选严格-mysql-tls)。 abbe89fb 原表结构预览、取消、旧计划拒绝与新计划执行通过。[V1076 当前原 Web 结构同步](testing/fixed-schema-sync-web-v831.md#v1076-当前发行候选原管理页结构同步)。 abbe89fb 共库与分库的原 HTTP 并发更新、导入回滚、撤权及初始化通过。[V1074 当前双拓扑事务与权限](testing/mysql-platform-topology-current-v461.md#v1074-当前发行包双拓扑并发导入与权限)。 abbe89fb 原审计默认、两种会话及宿主时区实库验收通过。[V1070 审计默认值与宿主时区](temporal-schema-compatibility.md#v1070-当前候选审计默认值与两种-linux-宿主时区)。 abbe89fb 的平台与示例目录末条写入故障、逐目录回滚和安装恢复已通过。[V1055 当前发行包种子回滚](testing/platform-seed-rollback-native.md#v1055-当前发行包两类目录末条种子回滚)。 abbe89fb 的原配置、实际下载、空库独立安装和原 CRUD 已通过。[V1054 当前生成下载与独立安装](testing/current-generated-delivery-v917.md#v1054-当前发行包配置下载及独立安装)。 abbe89fb 的第三业务库项目映射、原反查导入、非冗余能力与普通角色通过。[V1038 当前候选第三业务库实测](testing/schema-import-split-target-v555.md#v1038-当前发行平台第三业务库与存储能力验收)。 abbe89fb 的实际双 ZIP 通过三种 Production 安装及普通权限。[V1028 当前候选三拓扑](testing/storage-mode-linux-release-v1009-v1010.md#v1028-导航修复候选三拓扑实际安装)。 最新开发提交的非冗余原配置、实际下载、空库独立安装及新表原 CRUD 已通过。[V1022 非冗余配置到安装](testing/current-generated-delivery-v917.md#v1022-原接口非冗余配置到独立安装)。 原表配置 MySQL 非冗余选项已接通。当前开发源码的空库、能力查询及首次保存实库回归通过。[V1021 原表配置存储选项](nonredundant-storage.md#v1021-原表配置显示-mysql-非冗余选项)。 310a0ab3 的原平台配置、显式结构同步和两次实际下载通过。同一 Full ZIP 在独占空库安装 14 表，新表 CRUD 与重启通过。[V1018 当前生成到独立安装](testing/current-generated-delivery-v917.md#v1018-当前发行包生成下载及独立安装正式通过)。 310a0ab3 的实际 Linux 双包通过分库、共库及独立 Full 的 Production 空库安装、普通权限、CRUD 和重启。分库及共库的存储模式保存规则已由原接口实测。[V1009／V1010 发行候选](testing/storage-mode-linux-release-v1009-v1010.md)。 当前开发源码通过平台表配置存储模式兼容专项及 Production 双库自举。[V1008 表存储模式](nonredundant-storage.md#v1008-平台表存储模式保存兼容)。 当前独立第三业务库的项目映射、原反查导入及普通角色边界通过。[V1001–V1003 第三业务库实测](testing/schema-import-split-target-v555.md#v1001v1003-当前发行平台的第三业务库映射验收)。 当前 0b65780c 的反向导入双模型通过原配置、生成和独立应用正式运行。[V991 当前反查到生成](schema-import-draft.md#v991-当前发行源码原配置和生成复验)、[V994 当前反查包正式运行](testing/imported-application-production-v293.md#v994-当前反查完整包生产宿主复验通过)。 0b65780c 的原平台配置、显式结构同步和两次实际下载通过。同一 Full ZIP 在独占空库安装 14 表，新表 CRUD 与重启通过。[V987 当前生成到独立安装](testing/current-generated-delivery-v917.md#v987-当前发行包生成下载及独立安装正式通过)。 0b65780c 的当前包通过分库、共库和独立 Full 三种 Production 空库安装、重复保护、普通权限、CRUD 与重启，独立资源核清。见 [V944 当前三拓扑](testing/text-projection-runtime-topologies-v944.md)。 V917 由 68af5319 平台原接口实际下载 Full ZIP，两次 672 份文件一致，同一包严格发布并在独占空库安装 14 张表，原接口及新表业务通过。见 [V917 生成到独立安装](testing/current-generated-delivery-v917.md)。 68af5319 的两份 Linux ZIP 复建一致，三拓扑和正式 CLI 均已验。CLI 887 文件与 V893 实库包逐字一致。见 [V897 当前 Linux 候选](testing/current-linux-release-v897.md)。 1926a702 同批固定包已通过分库、共库和独立 Full 空库安装、真实普通查询、写入拒绝、重启与注销。正式 CLI 的混合键项目与 V887 实库用包逐字一致。见 [V888 当前 Linux 包](testing/current-linux-release-v888.md)。 先前 `ffcd80d3` 同一批发行包通过分库、共库和独立 Full 空库安装、真实业务、重复保护和重启。见 [V874 当前 Linux 包和三拓扑安装](testing/current-linux-release-v874.md)。 86775982 原管理页通过预览、取消、旧计划拒绝和新计划单次执行。见 [V831 固定候选原 Web 结构同步](testing/fixed-schema-sync-web-v831.md)。 86775982 原平台接口生成实际下载包，重复内容一致，同一 ZIP 安装并通过新表业务。见 [V824 从生成到独立安装](testing/fixed-candidate-generated-delivery-v824.md)。 86775982 三拓扑空库安装、重启和原接口业务通过。见 [V817—V820 固定发行包验收](testing/append-layout-release-verification-v817-v820.md)。 分库、共库及独立空库显式安装，普通启动零 DDL。由平台实际下载 Full 并独立安装，新配置表 CRUD 与重启通过。[V756](testing/leaf-public-release-install-v756.md)、[V760](testing/leaf-platform-download-install-v760.md)。修复后固定 ZIP 三拓扑复验通过。[V778 Linux 安装](testing/empty-trusted-proxy-linux-v778.md)。原管理页预览、取消、旧计划拒绝及重新执行通过。[V771](testing/integrated-schema-sync-web-v771.md) 最新 2b654bcd 三拓扑安装通过。见 [V793 安装与 V795 同父明细复验](testing/exact-lock-release-v793-v795.md)。 新 bd70b0e7 三拓扑实际安装、重复保护、原接口 CRUD、重启通过。见 [V806／V807 固定发行包](testing/parent-plan-linux-install-v806-v807.md)。 | 通过 |
| 平台与独立应用恢复 | e682cd12 的跨实例本地附件、旧 Web／OAuth 会话处置、同实例分库 SeaweedFS 原键恢复均通过。[V1156–V1157 当前分库恢复](testing/current-split-recovery-v927-v928.md#v1156v1157-当前发行候选同版分库恢复)。 abbe89fb 平台共库的单份 25 表快照及 SeaweedFS 原对象键恢复通过。旧对象端点移除后，按原逻辑位置验证公开及受保护下载。[V1119 平台共库 S3 恢复](testing/current-split-recovery-v927-v928.md#v1119-当前候选平台共库-s3-恢复)。 abbe89fb 共库平台已通过单快照跨 MySQL 实例恢复、旧 Web 与 Agent 凭据撤销及本地附件读回。[V1112 平台共库跨实例恢复](testing/current-split-recovery-v927-v928.md#v1112-当前候选平台共库跨实例恢复)。 abbe89fb 独立 Full 的跨实例本地附件及 Web／Agent 授权处置实测通过。[V1090 独立 Full 跨实例恢复](testing/current-split-recovery-v927-v928.md#v1090-当前独立-full-的-mysql-跨实例恢复)。 abbe89fb 独立 Full 的旧 Web 与 Agent 授权处置实测通过。[V1088 独立 Full 授权恢复](testing/current-split-recovery-v927-v928.md#v1088-当前独立-full-的-web-与-agent-授权恢复)。 abbe89fb 独立 Full 的 SeaweedFS 原键及会话恢复通过。[V1084 独立 Full 对象恢复](testing/current-split-recovery-v927-v928.md#v1084-当前候选独立-full-对象恢复)。 abbe89fb 生成的独立 Full 已通过同实例单库及两份本地附件恢复、旧 Web 会话处置和权限读回。[V1083 独立 Full 恢复](testing/current-split-recovery-v927-v928.md#v1083-当前候选独立-full-同版恢复)。 abbe89fb 的跨实例本地附件、旧 Web／OAuth 会话处置、同实例分库 SeaweedFS 原键恢复均通过。[V1041–V1042 当前分库恢复](testing/current-split-recovery-v927-v928.md#v1041v1042-当前发行候选同版分库恢复)。 0b65780c 当前宿主通过同版跨实例本地附件、旧 Web／OAuth 会话处置、同实例分库 SeaweedFS 原键恢复与独立回收。[V985–V986 当前分库恢复](testing/current-split-recovery-v927-v928.md#v985v986-当前发行源码分库恢复正式通过)。 68af5319 的 Windows Production 宿主通过跨实例本地附件恢复、旧 Web／OAuth 凭据撤销、计数不符回滚，以及同实例分库 SeaweedFS 原键恢复、异址拒绝和旧引用读回。见 [V927–V928 当前候选分库恢复](testing/current-split-recovery-v927-v928.md)。 86775982 的 Windows Production 宿主通过同版 MySQL 跨实例双库与本地附件恢复、旧 Web／OAuth 凭据撤销和计数不符回滚。同一候选通过同实例四库与 SeaweedFS 原键恢复、异址拒绝和旧引用读回。[V832–V834 固定候选分库恢复](testing/fixed-platform-recovery-v832-v834.md) | 固定范围通过 |
| 原桌面与移动 Web | 当前反查双模型的原桌面新增、复制和移动普通只读、撤权通过。[V1161 当前反查包原 Web](testing/imported-application-mobile-v553.md#v1161-当前两种反查包原桌面与移动-web-复验通过)。 e682cd12 的管理端和独立 Full 已完成当前发行物的双视口真实业务及会话读回。[V1147 当前双视口 Web](testing/current-exact-release-web-v913.md#v1147-当前发行物双视口-web-验收)。 e682cd12 两份正式 ZIP 完成 Web 构建和前序实测产物对照。[V1140 当前双包 Web 构建](testing/current-release-distribution-v963.md#v1140-当前候选双包-web-发行构建)。 当前独立 Full 的原移动客户增改删及普通会话通过隔离 HTTPS。[V1086 当前 Full 移动 HTTPS](testing/fixed-full-caddy-mobile-https-v844.md#v1086-当前发行-full-原移动-web-https-通过)。 当前发行平台的原桌面、原接口和代理审计已通过隔离 Caddy HTTPS 验收。[V1085 当前平台 HTTPS](testing/fixed-platform-caddy-https-v842-v843.md#v1085-当前发行平台-https-及原桌面-web-通过)。 abbe89fb 原合同选择器通过 5,014 候选的分页、字面搜索、桌面保存及移动回显。[V1057 当前合同大候选双视口](testing/current-contract-web-package-v948.md#v1057-当前合同大候选双视口复验)。 修复后原开发菜单在跨工作区仍可使用。桌面首次保存、移动重开和未绑定项目限制均经原操作通过。[V1026 原菜单到存储选项](nonredundant-storage.md#v1026-跨工作区原菜单完整浏览器复验)。 已修复跨工作区开发菜单缺失，三种工作区的原菜单接口实库通过。[V1025 原导航认证后台](nonredundant-storage.md#v1025-原导航按认证后台读取)。 原表单通过首次保存、移动重开和未绑定项目限制。跨工作区开发菜单缺失由 V1026 闭环。[V1023 原 Web 存储选项](nonredundant-storage.md#v1023-原-web-存储选项与跨工作区菜单缺口)。 310a0ab3 的平台实际下载包在原生成页通过桌面普通编辑与移动普通只读、独立 MySQL 和资源核验。[V1019 当前实际下载包原 Web](testing/current-downloaded-package-web-v918.md#v1019-当前实际下载包原-web-正式通过)。 当前独立 Full 的原移动客户增改删及普通会话通过隔离 HTTPS。[V998 当前 Full 移动 HTTPS](testing/fixed-full-caddy-mobile-https-v844.md#v998-当前发行-full-原移动-web-https-通过)。 当前发行平台的原桌面、原接口和代理审计已通过隔离 Caddy HTTPS 验收。[V997 当前平台 HTTPS](testing/fixed-platform-caddy-https-v842-v843.md#v997-当前发行平台-https-及原桌面-web-通过)。 当前反查双模型的原桌面新增、复制和移动普通只读、撤权通过。[V996 当前反查包原 Web](testing/imported-application-mobile-v553.md#v996-当前两种反查包原桌面与移动-web-复验通过)。 0b65780c 的平台实际下载包在原生成页通过桌面普通编辑与移动普通只读、独立 MySQL 和资源核验。[V988 当前实际下载包原 Web](testing/current-downloaded-package-web-v918.md#v988-当前实际下载包原-web-正式通过)。 0b65780c 的原合同选择器已通过 5,014 候选的桌面与移动 Web。见 [V948 当前选择器](testing/current-contract-web-package-v948.md)。 管理端和独立 Full 普通只读、编辑的六条桌面与移动 Web 流程、MySQL 读回和资源清理已通过。[V976 双视口实测](testing/current-exact-release-web-v913.md#v976-正式结果)。 V918 由原平台实际下载的 68af5319 Full ZIP 通过原桌面普通编辑、移动普通只读和独立 SQL 核对。见 [V918 下载包双视口](testing/current-downloaded-package-web-v918.md)。 68af5319 分库管理端与独立 Full 普通只读、编辑均通过原桌面和移动 Web，六会话零存留并完成独立数据库读回。见 [V913 当前候选双视口 Web](testing/current-exact-release-web-v913.md)。 86775982 的原平台桌面通过隔离 Caddy HTTPS、真实登录、项目菜单及退出。见 [V842–V843 固定候选平台 HTTPS](testing/fixed-platform-caddy-https-v842-v843.md)。 同一候选的独立 Full 通过移动 Web 隔离 HTTPS、普通客户 CRUD、退出及独立读回。见 [V844 固定候选独立 Full 移动 HTTPS](testing/fixed-full-caddy-mobile-https-v844.md)。 V940 用 68af5319 对同一合同模型复验两轮生成、宿主严格编译及 395 份原前端一致，当前浏览器场景仍待执行。见 [V940 当前生成对照](testing/current-contract-selector-package-v940.md)。86775982 的原合同选择器通过 5,014 条候选的桌面滚动、字面搜索、长 ID 保存和移动读回，两端旧全量调用为零。见 [V830 固定候选合同选择器](testing/fixed-contract-options-web-v830.md)。 86775982 从平台实际下载的 ZIP 通过原生成页面桌面业务和移动只读，独立核对长 ID、物理记录与注销。见 [V825 固定候选原下载包 Web](testing/fixed-generated-package-web-v825.md)。 管理端和独立 Full 的两个视口通过，实际下载包验证桌面编辑、移动只读、字符串 ID 和注销。[V752](testing/leaf-public-release-browser-v752.md)、[V761](testing/leaf-downloaded-package-web-v761.md)。修复后候选的两类应用及两种视口已复验。[V779 固定包双视口 Web](testing/patched-release-browser-v779.md)。 最新 2b654bcd 两套 Web 重新导出通过，同提交原浏览器双视口通过。见 [V802 固定包双视口 Web](testing/exact-fresh-web-browser-v802.md)。见 [V801 Web 独立导出](testing/exact-web-rebuild-v801.md)。 两应用的本机登录渲染已核对。见 [V804／V805 Web 与测试清理](testing/web-login-and-test-cleanup-v804-v805.md)。 | e682cd12 原管理端和独立 Full 双视口业务通过 |
| 关系、Excel 与文件 | e682cd12 公开 Full 的六模型追加、覆盖、锁等待及整批文件回滚已通过。[V1154 当前公开六模型追加与覆盖](testing/fixed-cross-excel-v826.md#v1154-当前公开-full-六模型追加与覆盖)。 e682cd12 的原选档、整批回滚、原工作簿下载及移动只读回查通过。[V1153 当前原 Web Excel](testing/integrated-original-web-excel-v713.md#v1153-当前候选原-web-excel)。 e682cd12 的分库与共库 S3 附件真实验收通过。[V1136／V1137 当前 S3 附件验收](testing/current-split-s3-file-race-v925.md#v1136v1137-当前候选分库与共库-s3-验收)。 abbe89fb 原移动 Web 的目录新建、改名、移动、拒绝及空目录软删通过。[V1123 原移动 Web 文件库目录](original-file-management.md#v1123-当前候选原移动-web-文件库目录管理)。 abbe89fb 的真实共库 S3 竞态、回滚、撤权及独立清理通过。[V1121 当前共库 S3](testing/current-split-s3-file-race-v925.md#v1121-当前候选共库-s3-附件竞态)。 abbe89fb 已在三库验证已发布文件槽、实际附件字节和未发布项目拒绝。[V1115 三库文件路由](testing/schema-import-split-target-v555.md#v1115-当前候选三库文件路由验收)。 abbe89fb 四种来源与父键的跨表 Full 已通过正式空库、普通身份、撤权、附件及事务。 [V1106 跨表 Full 正式空库](nonredundant-cross-table-fresh.md#v1106-当前候选跨表-full-空库与正式身份)。 abbe89fb 原生成文件 HTTP 的非冗余逻辑父范围、复制、导入回滚及删除在独占 MySQL 通过。[V1102 逻辑父文件事务](nonredundant-cross-table-parent-files.md#v1102-当前候选独占库的非冗余逻辑父文件事务)。 abbe89fb 的非冗余跨表父范围及 Excel 四项实库通过。[V1099 非冗余跨表实库](nonredundant-cross-table-parent.md#v1099-当前候选非冗余跨表父范围与-excel-实库复验)。 abbe89fb 五项原级联及跨表读／筛选真实 MySQL 复验通过。[V1096 原级联实库](cascade-tree-read-contract.md#v1096-当前候选五项原级联-mysql-复验)。 abbe89fb 六模型原桌面选档及移动 Web 详情回查通过。[V1075 六模型双视口回查](testing/fixed-cross-overwrite-web-v827.md#v1075-六模型原桌面导入与移动-web-回查)。 abbe89fb 共库与分库文件事务 18 项实库通过。[V1073 当前文件事务双拓扑](testing/fixed-candidate-file-transactions-v823.md#v1073-当前发行候选文件事务双拓扑)。 abbe89fb 原移动 Web 公开文件改名、移动、引用拒绝及解绑后物理删除通过。[V1072 原移动 Web 文件管理](original-file-management.md#v1072-当前候选原移动-web-文件改名移动与删除)。 abbe89fb 固定 R0 模型的 501 叶实库批读、两份 506 行原 XLSX 及重名拒绝通过。[V1071 当前 R0 实库批读](testing/cross-table-excel-batch.md#v1071-当前公开-full-的-r0-实库批读与原导出)。 abbe89fb 原桌面公开文件改名、移动、引用拒绝及解绑后物理删除通过。[V1069 原 Web 文件管理](original-file-management.md#v1069-当前候选原-web-文件改名移动与删除)。 abbe89fb 原桌面文件库目录新建、重命名、移动、拒绝及空目录软删通过。[V1068 原 Web 文件库目录](original-file-management.md#v1068-当前候选原-web-文件库目录管理)。 abbe89fb 六模型原桌面有效追加、末行拒绝及 24 条记录核对通过。[V1067 当前原 Web 六模型追加](testing/fixed-cross-overwrite-web-v827.md#v1067-当前候选六模型原-web-追加与拒绝)。 abbe89fb 六模型原桌面选档、有效覆盖及末行拒绝已通过。[V1066 当前原 Web 六模型覆盖](testing/fixed-cross-overwrite-web-v827.md#v1066-当前候选六模型原-web-覆盖与拒绝)。 abbe89fb 三种部署方式的原桌面、移动双附件及桌面取消和保存解绑均通过。[V1065 当前独立 Full 桌面附件与解绑](testing/fixed-standalone-s3-mobile-media-v860-v861.md#v1065-当前独立-full-原桌面附件与解绑)。 abbe89fb 平台共库桌面原附件取消和保存解绑通过。[V1064 当前平台共库桌面附件与解绑](testing/fixed-shared-s3-mobile-media-v854-v859.md#v1064-当前平台共库原桌面附件与解绑)。 abbe89fb 平台分库桌面原附件上传、下载、取消与保存解绑通过。[V1063 当前平台分库桌面附件与解绑](testing/fixed-split-s3-mobile-object-v852-v853.md#v1063-当前平台分库原桌面附件与解绑)。 abbe89fb 的独立 Full、平台分库和平台共库均已通过原移动双附件及双普通角色，共库未发布哨兵库零表。[V1062 当前平台共库移动双附件](testing/fixed-shared-s3-mobile-media-v854-v859.md#v1062-当前平台共库原移动双附件)。 abbe89fb 平台分库原移动双附件、两种普通角色与物理归属通过。[V1061 当前平台分库移动双附件](testing/fixed-split-s3-mobile-object-v852-v853.md#v1061-当前平台分库原移动双附件)。 abbe89fb 独立 Full 的原移动 Web 双附件、两种普通角色及独立 MySQL／S3 通过。[V1060 当前独立 Full 移动双附件](testing/fixed-standalone-s3-mobile-media-v860-v861.md#v1060-当前候选独立-full-原移动双附件)。 abbe89fb 公开 Full 的六模型追加、覆盖、锁等待及整批文件回滚已通过。[V1056 当前公开六模型追加与覆盖](testing/fixed-cross-excel-v826.md#v1056-当前公开-full-六模型追加与覆盖)。 abbe89fb 的原选档、整批回滚、原工作簿下载及移动只读回查通过。[V1044 当前原 Web Excel](testing/integrated-original-web-excel-v713.md#v1044-当前候选原-web-excel)。 abbe89fb 的真实分库 S3 竞态、回滚、撤权及独立清理通过。[V1037 当前分库 S3](testing/current-split-s3-file-race-v925.md#v1037-当前候选分库-s3-附件竞态)。 310a0ab3 的独立 Full 普通编辑完成原复选分组联动、桌面与移动保存重开，普通查询账号回显及实库通过。[V1020 当前原复选详情联动](testing/generated-multiple-controls-browser-v452-v453.md#v1020-当前候选原复选详情双视口联动)。 310a0ab3 的原选档、整批回滚、原工作簿下载及移动只读回查通过。[V1017 当前原 Web Excel](testing/integrated-original-web-excel-v713.md#v1017-当前候选原-web-excel)。 310a0ab3 的真实分库 S3 竞态、回滚、撤权及独立清理通过。[V1016 当前分库 S3](testing/current-split-s3-file-race-v925.md#v1016-当前候选分库-s3-附件竞态)。 0b65780c 的原选档、整批回滚、原 XLSX 下载及移动只读回查通过。[V984 当前原 Web Excel](testing/integrated-original-web-excel-v713.md#v984-当前发行包原-web-excel-正式通过)。同一源码的 Windows Production 宿主通过真实分库 S3 双顺序竞态、回滚和普通账号撤权。[V979 当前分库 S3](testing/current-split-s3-file-race-v925.md#v979-当前发行源码分库附件正式通过)。 V925 在 68af5319 发行源码构建的 Windows Production 宿主上通过真实分库 S3 绑定／删除竞态、回滚及原会话撤权。见 [V925 当前候选分库 S3](testing/current-split-s3-file-race-v925.md)。 V887 公开 Full 的六种混合关系键通过三级父范围、部分路径、原 XLSX、权限和 36 批回滚。见 [V887 混合键父级 Excel](testing/public-mixed-parent-excel-v887.md)。 V885 公开 Full 通过纯 bigint 三级逻辑父范围的追加、覆盖、列表／详情 XLSX、六批回滚和独立回收。见 [V885 bigint 父级 Excel](testing/public-int64-parent-excel-v885.md)。 V873 公开 Full 的三级逻辑父范围详情导出通过原工作簿列、跨父选择隔离和三级权限验证。见 [V873 公开 Full 三级父范围详情导出](testing/public-logical-parent-detail-export-v873.md)。 V872 公开 Full 的三级逻辑父范围物理文本首列覆盖通过原 XLSX、同名隔离、重名行列反馈、整批回滚和运行时升级。见 [V872 公开 Full 三级父范围首列覆盖](testing/public-logical-parent-overwrite-v872.md)。 V871 内部准备包通过三级逻辑父范围按首列覆盖、同名隔离和原 XLSX，公开 Full 及重名提示继续。见 [V871 三级逻辑父范围首列覆盖](testing/logical-parent-overwrite-cross-excel-v871.md)。 V870 公开 Full 已通过中层和末层的追加／列表导出、跨父整批回滚、原 SheetJS 及稀疏存储读回。见 [V870 公开 Full 中层与末层 Excel](testing/public-middle-leaf-parent-cross-excel-v870.md)。 V869 在 d13e21ab 隔离准备包验证中层和末层父范围的原 XLSX、越界整批回滚和独立稀疏读回。公开 Full 继续。见 [V869 中层及末层父范围 Excel](testing/middle-leaf-parent-cross-excel-v869.md)。 V868 公开 Full 已验收字符串逻辑根父范围追加和列表导出、原 SheetJS 往返及跨父整批回滚。见 [V868 公开 Full 根父范围 Excel](testing/public-root-parent-cross-excel-v868.md)。 86775982 的内部准备包通过逻辑根父范围 Excel、跨父批次回滚和原 SheetJS 往返，公开 Full 继续验收。见 [V866–V867 来源父范围级联 Excel](testing/parent-scoped-cross-excel-v866-v867.md)。 86775982 在真实 MySQL 和 SeaweedFS 下通过绑定先等待、删除先等待与原会话撤权。两组实际行锁、事务回滚、文件引用归属和 S3 独立对象核对通过。见 [V862 固定候选分库 S3 竞态与撤权](testing/fixed-split-s3-file-race-v862.md)。 86775982 真实 SeaweedFS 下通过分库与共库原 HTTP、普通权限、对象回滚和同址重启读回。见 [V828／V829 固定候选平台 S3](testing/fixed-platform-s3-v828-v829.md)。 同一候选分库移动 Web 的普通编辑和只读角色各完成单次受保护 ZIP 下载。同轮独立 MySQL、S3 对象字节读回、匿名拒绝及测试桶清理通过。见 [V852–V853 固定候选分库移动附件同轮闭环](testing/fixed-split-s3-mobile-object-v852-v853.md)。 同一候选共库移动 Web 验证双普通角色对公开 PNG 的原预览和受保护 ZIP 的单次下载，并独立核对两个对象、匿名边界及空哨兵库。见 [V854–V859 固定候选共库移动双附件](testing/fixed-shared-s3-mobile-media-v854-v859.md)。 同一候选的独立 Full 通过空库显式安装、双普通角色原移动 PNG 预览及 ZIP 下载、两个独立 S3 对象和匿名 401 边界。见 [V860–V861 固定候选独立 Full 移动双附件](testing/fixed-standalone-s3-mobile-media-v860-v861.md)。 86775982 六模型原桌面选档覆盖与无效末行均通过，独立核对 18 条记录。见 [V827 固定候选六模型原 Web 覆盖](testing/fixed-cross-overwrite-web-v827.md)。 86775982 公开 Full 六模型原 Excel 追加与覆盖通过，核对关系传播、真实锁等待和文件引用整批回滚。见 [V826 固定候选六模型 Excel](testing/fixed-cross-excel-v826.md)。 86775982 在真实 MySQL 中通过分库与共库文件事务 18 项，覆盖关系传播与文件换绑共同回滚。见 [V823 固定候选文件事务](testing/fixed-candidate-file-transactions-v823.md)。 六模型原 XLSX 追加、按首列覆盖、改名传播、维护、并发锁及整批回滚。分库和共库文件事务与 S3 独立复验。[V767](testing/leaf-public-r0-cross-excel-v767.md)、[V768 原 Web 六模型覆盖](testing/integrated-cross-overwrite-web-v768.md)、[V757–V759](testing/leaf-platform-file-transactions-s3-v757-v759.md) | 固定范围通过 |
| 权限与工具 | e682cd12 的原浏览器批量按钮通过部分失败、完整成功及刷新后原批次只读恢复。见 [V1152 当前原 Web 批量恢复](testing/fixed-device-browser-v838.md#v1152-当前发行源码原-web-批量同步与回执恢复通过)。 e682cd12 的在线助手原批量接口、双项目真实应用及原批次重启已通过。[V1151 当前在线助手双工程](testing/fixed-device-runtime-v837.md#v1151-当前发行源码在线助手双工程与原批次重启通过)。 e682cd12 的分库、共库及独立 Full 已完成 126 个真实 MCP 请求，独立核对权限、数据和资源回收。[V1144–V1146 当前三拓扑 MCP](testing/current-platform-full-mcp-v937-v938.md#v1144v1146-当前候选三拓扑-mcp-权限)。 abbe89fb 的共库、分库及公开独立 Full 已通过真实 MCP，合计 126 个 HTTP 请求。[V1078 当前共库 MCP](testing/current-platform-full-mcp-v937-v938.md#v1078-当前候选共库-mcp-项目隔离)。 abbe89fb 共库与分库的原后台账号初始化、并发、末段回滚及分库当前 Agent 门禁通过。[V1059 当前双拓扑后台账号初始化](testing/platform-admin-agent-replay-v479.md#v1059-当前发行包双拓扑后台账号初始化)。 abbe89fb 的完整管理包通过原网页登录回跳、Agent 授权与撤权，以及真实 CLI、MCP 和重启。[V1052 当前原网页授权与撤权](testing/fixed-management-oauth-web-v841.md#v1052-当前完整管理包原网页授权与撤权通过)。 abbe89fb 的原浏览器批量按钮通过部分失败、完整成功及刷新后原批次只读恢复。见 [V1051 当前原 Web 批量恢复](testing/fixed-device-browser-v838.md#v1051-当前发行源码原-web-批量同步与回执恢复通过)。 abbe89fb 的在线助手原批量接口、双项目真实应用及原批次重启已通过。[V1050 当前在线助手双工程](testing/fixed-device-runtime-v837.md#v1050-当前发行源码在线助手双工程与原批次重启通过)。 abbe89fb 的完整管理包通过三账号三授权、真实 CLI 与 MCP、保留 ID 初始化和重启。[V1048 当前完整管理包](testing/fixed-management-native-v840.md#v1048-当前发行源码完整管理包正式通过)。 abbe89fb 的平台分库与公开 Full 已通过真实 OAuth、MCP 只读、跨项目拒绝和撤权。[V1046–V1047 当前候选 MCP](testing/current-platform-full-mcp-v937-v938.md#v1046v1047-当前候选-mcp-实库权限复验)。 当前完整管理包通过 CLI 同址重启、原令牌及任务回读、刷新轮转和重复写入拒绝。[V1007 Agent 同址重启](standalone-agent.md#v1007-当前完整管理包-agent-同址重启)。 当前原浏览器设备链接登录回跳、批准和撤销已通过。[V1006 当前原浏览器授权](testing/fixed-management-oauth-web-v841.md#v1005v1006-当前发行管理包原浏览器授权复验)。 当前完整管理包的三账号／三授权、真实 CLI／MCP、保留 ID 初始化和重启已通过。[V1004 当前完整管理包](testing/fixed-management-native-v840.md#v1004-当前发行源码完整管理包正式通过)。 当前原浏览器批量按钮通过部分失败、完整成功及刷新后原批次只读恢复。见 [V1000 当前原 Web 批量恢复](testing/fixed-device-browser-v838.md#v1000-当前发行源码原-web-批量同步与回执恢复通过)。 当前在线助手的原批量接口、双项目真实应用与原批次重启已通过。[V999 当前在线助手双工程](testing/fixed-device-runtime-v837.md#v999-当前发行源码在线助手双工程与原批次重启通过)。 0b65780c 的平台分库和独立 Full 通过真实 OAuth、MCP 只读边界、跨项目拒绝与撤权，业务行及任务账本独立核对。[V989–V990 当前 MCP 实库权限](testing/current-platform-full-mcp-v937-v938.md#v989v990-当前发行源码真实权限复验通过)。 86775982 在真实平台双发布目录完成 MCP 正向对照、演示项目 Agent 跨项目拒绝、普通业务读取及分库零任务核验。见 [V865 固定平台 MCP 双发布目录](testing/fixed-platform-mcp-project-boundary-v865.md)。 86775982 的真实 OAuth Agent 在公开独立 Full 的 MCP 仅可读，写入结构与确认调用、错误项目和旧授权均拒绝。见 [V864 固定独立 Full Agent MCP](testing/fixed-standalone-mcp-agent-v864.md)。 86775982 公开独立 Full 的普通 Web 会话通过真实 MCP 请求结构、权限收窄与撤销和 DTO 物理读回。见 [V863 固定独立 Full MCP 结构](testing/fixed-standalone-mcp-schema-v863.md)。 86775982 的原浏览器设备链接登录回跳、授权与撤销通过。见 [V841 固定候选原浏览器 OAuth](testing/fixed-management-oauth-web-v841.md)。 86775982 的独立完整管理包通过真实 OAuth／CLI／MCP、撤权、保留 ID 初始化及重启。见 [V840 固定候选独立完整管理包](testing/fixed-management-native-v840.md)。 86775982 的原浏览器批量按钮通过部分失败、完整成功及刷新后原批次只读恢复。见 [V838 固定候选原 Web 批量恢复](testing/fixed-device-browser-v838.md)。 86775982 的原在线助手通过真实双项目同步、写入范围和同地址重启。见 [V837 固定候选双项目在线助手](testing/fixed-device-runtime-v837.md)。 完整管理包、OAuth CLI、MCP、初始化和重启。原浏览器设备链接登录回跳、授权与撤销通过。[V775 原浏览器 OAuth](testing/leaf-management-oauth-browser-v775.md)。独立完整包的授权列表与撤销隔离通过。[V776 授权隔离](testing/leaf-management-grant-isolation-v776.md)。原工作台普通编辑者权限保存及库读回通过。同项目角色列表和异后台工作台读写边界已核对。[V774 角色后台范围](testing/role-realm-parity-v774.md)。[V772 原管理工作台](testing/leaf-management-browser-v772.md)。在线助手真实工程写入、手写保护与原浏览器回执恢复。[V762](testing/leaf-management-package-v762.md)、[V765](testing/leaf-device-runtime-v765.md)、[V766](testing/leaf-device-browser-v766.md) | 固定范围通过 |
| 同条件性能 | e682cd12 的九类最新正式结果达到原门槛，保留 V1130 原逐次修复失败。[V1139 逐次修复正式复验](testing/current-mysql-performance-v1130.md#v1139-逐次修复同条件正式复验)。 e682cd12 九类实际完成。[V1130 九类正式结果](testing/current-mysql-performance-v1130.md)。 e682cd12 逐次修复触发稳定退化。[V1130 逐次修复性能阻断](testing/current-release-child-performance-v957-v958.md#v1130-当前候选逐次修复性能阻断)。 e682cd12 首项通过。[V1129 同合同明细复测](testing/current-release-child-performance-v957-v958.md#v1129-当前候选同合同明细性能)。 abbe89fb 的 45,756 个正式请求、66 个配对轮次、九类门槛均已核对。[V1032 当前候选九类](testing/current-mysql-performance-v1032.md)。 abbe89fb 同合同明细对照已核验。[V1031 同合同明细](testing/current-release-child-performance-v957-v958.md#v1031-当前候选同合同明细导入)。 V974 当前发行包 0b65780c 的 45,756 个正式请求、66 个配对轮次全部核对，九类均无稳定退化。[V974 九类正式结果](testing/current-release-child-performance-v957-v958.md#v974-当前发行包九类正式验收结果)。 V970 用同一套工具顺序对照两版，当前 0b65780c 的同合同明细导入单并发与四并发均 0／3 触线，V958 有效退化记录保留，当前包完整九类继续。见 [V970 正式结果](testing/current-release-child-performance-v957-v958.md#v970-两版同条件正式结果)。 V958 用当前 0b65780c 的实际发行载荷复验同合同明细，四并发两轮吞吐触线。见 [V957／V958 当前发行包](testing/current-release-child-performance-v957-v958.md)。 V955 定位写后核验，四并发每批 2.64–2.79 毫秒，三轮吞吐继续触线。见 [V955 逐行核验计时](testing/current-child-packed-receipt-timing-v955.md)。 V936 实库两组刷盘对照均四并发 3／3 触线，正式门槛仍阻断。见 [V936 刷盘对照](testing/current-redo-flush-control-v936.md)。 V935 从 V929 原始窗口复算，同合同四并发每百批需缩短 1.08 至 1.49 秒才达吞吐线，前台 binlog 同步仅解释部分提交差值。见 [V935 性能差距复算](testing/current-child-commit-budget-v935.md)。 V929 父快照复用候选通过实库功能，四并发 3／3 触线，隔离补丁留证。见 [V929 父快照复用候选](testing/same-parent-snapshot-reuse-v929.md)。 V926 单独切换 redo 写入线程，两组同条件诊断已完成。开启和关闭两组的四并发均出现稳定退化。见 [V926 redo 线程对照](testing/current-redo-writer-control-v926.md)。 V924 的隔离对照确认 C# 每百笔 binlog 减少 90.6%，两种模式均四并发 3／3 触线，正式门槛仍阻断。见 [V924 日志压缩对照](testing/current-binlog-compression-v920-v924.md#v924-正式对照结果)。 V916 对照 V819 历史通过轮与 V899，Data 的 3,057 个方法体与 8 份基准宿主载荷一致，历史缺 7 项持久化设置证据，当前门槛仍阻断。见 [V916 历史通过轮复核](testing/current-child-historical-pass-v916.md)。 V915 两组隔离对照均复现四并发稳定退化，逐笔核对父锁、提交和缓存写入。正式门槛继续按 V912 判定。见 [V915 同合同隔离对照](testing/current-child-isolated-control-v915.md)。 V912 当前固定候选九类已全部测量，共 45,756 个正式请求、66 个配对轮次，8 类达标。同合同明细导入出现稳定退化，首版性能仍阻断。见 [V912 当前候选九类性能](testing/current-exact-mysql-performance-v912.md)。 V911 同一候选 Excel 导出完成 2,592 个正式请求，两个并发档均无触线。当前九类已测八类。见 [V911 当前候选 Excel 导出性能](testing/current-exact-excel-export-performance-v911.md)。 V909 同一候选受保护附件下载完成 4,800 个正式请求和 300 MiB 内容核对，两档均无稳定退化。当前九类已测七类。见 [V909 当前候选受保护下载性能](testing/current-exact-protected-download-performance-v909.md)。 V907 同一候选父级 Excel 导入完成 864 个正式请求，两个并发档均未出现稳定退化。当前九类已测六类。见 [V907 当前候选父级 Excel 导入](testing/current-exact-parent-excel-performance-v907.md)。 V906 同一候选普通写入完成 8,400 个正式请求，两档均无稳定退化。当前九类已测五类。见 [V906 当前候选普通写入性能](testing/current-exact-write-performance-v906.md)。 V903 同一候选只读完成 19,200 个正式请求，两档均无稳定退化。当前九类已测四类。见 [V903 当前候选只读性能](testing/current-exact-read-performance-v903.md)。 V901 同一候选逐次修复 144 个、整表维护 156 个正式请求，分别未出现稳定退化、未出现稳定退化；当前九类已测三类。见 [V901 当前候选维护性能](testing/current-exact-maintenance-performance-v901.md)。 V899 用 68af5319 发行包派生镜像正式复验同合同默认导入，四并发两轮触线，首版性能仍阻断。V898 镜像和 V900 其余八类离线范围已固定。见 [V898／V899 当前候选性能](testing/current-exact-child-performance-v898-v899.md)。 V902 对照两轮的七个公共程序集，8,849 个方法体一致。见 [V902 两轮载荷与计时](testing/current-exact-child-performance-v898-v899.md#v902-两轮载荷与绝对耗时复核)。 V896 用原 V875 固定镜像及新版测量器复验同合同明细，1,200 个正式请求和 24 个窗口边界核对，两档均零轮触线。V875 历史两轮触线保留，当前最终整包仍待九类矩阵。见 [V896 默认路径复验](testing/current-child-durable-replay-v896.md)。 V895 已将七项持久化和组提交设置纳入逐窗核验，77 项测量回归与实际只读解析通过；正式负载未重跑。见 [V895 持久化取证](testing/mysql-performance-durability-v895.md)。 当前 1926a702 与 V875 基线的七个运行时程序集共 8,849 个归一化方法体一致，默认锁及缓存写入未改变；当前候选正式计时仍待。见 [V890 运行时对照](testing/current-runtime-performance-baseline-v890.md)。 V886 的完整 HTTP 排队试验降低数据库父锁读取时间，端到端四并发仍三轮触线。见 [V886 同父明细排队诊断](testing/current-child-admission-v886.md)。 V881 验证减少日志后排他父锁仍是主要等待，隔离试验未通过门槛。见 [V881 排他锁单次写入对照](testing/current-child-exclusive-inline-v881.md)。 V880 已配置共享追加时四并发零轮触线，默认路径仍待通过。见 [V880 共享追加与日志量](testing/current-child-shared-log-volume-v880.md)。 V879 实测同父导入的 binlog 增量为 Java 的 2.44 倍，redo 写入为 1.92–2.11 倍，未改变正式门槛。见 [V879 同父导入日志量](testing/current-child-log-volume-v879.md)。 V878 独立复算 V876 缓存来源读和更新的直接耗时，以及取锁后至提交结束的差值。见 [V878 当前 Windows 与缓存时序](testing/current-windows-and-cache-timing-v878.md)。 V877 通过原生成 HTTP 的合并计数功能验收。候选四并发仍达到稳定退化条件。本轮改动保留为隔离试验，开发分支继续使用已验基线。见 [V877 合并插入计数试验](testing/packed-insert-count-v877.md)。 V876 用同一候选的 12 个完整 SQL 诊断窗口确认四并发差异集中在父锁读，默认权限下缓存保留两阶段写入。见 [V876 当前同父导入尾延迟诊断](testing/child-import-tail-diagnostic-v876.md)。 当前 ffcd80d3 完成 45,756 个正式请求和 66 个配对轮次。稳定退化项为同父合同明细追加，当前性能门槛未通过。见 [V875 当前候选九类同条件性能](testing/current-mysql-performance-v875.md)。 86775982 完成 45,756 个正式请求及 66 个配对轮次，九类均达标。见 [V819 九类同条件性能](testing/append-layout-mysql-performance-v819.md)。 V816 合并结构核验候选完成同条件专项，四并发 1／3 轮触线。见 [V815／V816 合并结构核验](testing/append-layout-roundtrip-v815-v816.md)。 V811 隔离分段的最大差值位于父锁读调用。见 [V811／V814 事务分段与 Windows 回归](testing/parent-plan-diagnostic-windows-v811-v814.md)。 V808 固定 bd70b0e7 的同父明细四并发两轮触线，阻断该历史候选。见 [V808 固定整包性能](testing/parent-plan-exact-performance-v808.md)。 V800 两文件覆盖专项达到原门槛，整包矩阵待复验。见 [V799／V800 父候选计划复用](testing/parent-plan-reuse-v799-v800.md)。固定 2b654bcd 完成 45,756 个正式请求和 66 个配对轮次。构成稳定退化的场景为同父合同明细 Excel 追加，首版性能仍未签收。见 [V795 九类同条件性能矩阵](testing/exact-lock-mysql-performance-v795.md)。V782 用修复后候选 a88bbc89 核对 45,756 个正式请求和 66 个配对轮次。稳定退化场景为同父合同明细 Excel 追加，继续阻断性能签收。见 [V782 修复后性能矩阵](testing/patched-mysql-performance-v782.md)。独占诊断四并发三轮均触线，事务分段已复算。[V784 同父明细事务分段](testing/patched-mysql-child-phases-v784.md)。COMMIT 时差主要在 waiting for handler commit。[V790 同父明细 COMMIT 分段](testing/patched-mysql-child-commit-waits-v790.md)。V792 两文件覆盖专项六轮均达标，后续新整包结果以 V795 为准。[V791／V792 精确锁读候选](testing/packed-exact-lock-read-v791-v792.md)。旧基线见 [V748](testing/exact-mysql-performance-v748.md)。 | 最新正式复验达标，初测退化留证 |
| Windows 本机回归 | e682cd12 固定源码 30 组通过。[V1131 当前 Windows 回归](testing/current-windows-regression-v946-v947.md#v1131-当前候选-windows-固定回归)。 abbe89fb 的完整生成器回归六组已独立核对，2,878 通过、273 条件跳过、零失败，七个专用目录均已回收。[V1039 当前 Windows 六组](testing/current-windows-generation-full-v921.md#v1039-当前候选六组全部完成)。 abbe89fb 固定源码的 30 组回归已完成。[V1029 当前 Windows 回归](testing/current-windows-regression-v946-v947.md#v1029-当前候选-windows-固定回归)。 V921 当前候选 Windows 六组共 3,150 用例，2,877 通过、273 条件跳过，零失败，七个专属根核清。见 [V921 Windows 全量生成](testing/current-windows-generation-full-v921.md)。 68af5319 的 Windows 30 组通过，147 个定向生成用例及 717 份再生成与同步文件核对通过。见 [V904 当前候选 Windows 回归](testing/current-windows-release-v904.md)。 ffcd80d3 的 Windows 30 组通过，86 个定向生成用例全部通过，717 份产物逐字一致。见 [V878 当前 Windows 与缓存时序](testing/current-windows-and-cache-timing-v878.md)。 86775982 本机 30 组、49 个定向生成用例及 717 份重新生成和同步通过。见 [V817—V820 固定发行包验收](testing/append-layout-release-verification-v817-v820.md)。 bd70b0e7 九套、观察器、原前端与定向生成通过，717 文件再生成一致。见 [V811／V814 事务分段与 Windows 回归](testing/parent-plan-diagnostic-windows-v811-v814.md)。 平台九套及原前端回归。六组生成回归覆盖独立枚举的 3,072 个用例，2,799 项通过，273 项按环境条件跳过。[V755](testing/leaf-windows-platform-v755.md)、[V751](testing/leaf-windows-generation-v751.md) 修复后 Windows 平台、原前端和 717 份再生成产物已复验。[V783 Windows 平台回归](testing/patched-windows-platform-v783.md)。 修复后 74411104 的六组用例再次全部核对。见 [V786 Windows 生成回归](testing/patched-windows-generation-v786.md)。 | e682cd12 固定源码 30 组通过，完整生成器六组进行中 |
| 最终 GitHub CI | e682cd12 的官方 CI 15 项通过，14 份报告已核对。[V1138 当前候选 GitHub CI](testing/ci-current-contracts.md#v1138-当前候选-github-ci)。 abbe89fb 的 GitHub CI 15 项全部通过。[当前候选 CI](https://github.com/zhao-yc/metarill/actions/runs/42965876964)。 独立 Linux 前 57 批原始为 2,860 通过、274 条件跳过、17 失败，均有同名同 ID 的独立通过证据。计入复验后，3,150 项与 Windows 结果一致，另 1 项为 Windows 专属用例。[V1120 两系统前 57 批](testing/current-linux-generation-batches-v945.md#v1120-当前候选-linux-第-57-批逐项核对)。 当前 Linux 严格构建通过，发现 3,151 项，首批 15 项通过。[V1045 Linux 首批](testing/current-linux-generation-batches-v945.md#v1045-当前候选-linux-构建与首批通过)。 独立 Linux 已缩小容器限额并越过内存门槛。原命令单参数超限，完整输入按命令文件复验。[V1043 Linux 命令传递复验](testing/current-linux-generation-batches-v945.md#v1043-当前候选-linux-命令传递复验)。 abbe89fb 的独立 Linux 离线 npm 通过，.NET 因启动前可用内存未达门槛而未运行。[V1034 当前 Linux 启动检查](testing/current-linux-generation-batches-v945.md#v1034-当前候选-linux-回归启动检查)。 abbe89fb 对应的 [PR CI](https://github.com/zhao-yc/metarill/actions/runs/42965876964) 已开始执行 15 个作业，最终结果待核验。 [0b65780c 的 GitHub CI](https://github.com/zhao-yc/metarill/actions/runs/42792033491) 15 作业全部成功。Linux 第 4 组原 855 项按 ID 合并为 731 通过、124 条件跳过、零失败。Server-200 独立 Linux 第 5 组 466 通过、19 条件跳过、零失败，六组合并为 2877 通过、274 条件跳过、零失败，测试夹具提交单独跟踪。[第 5 组回执](testing/current-linux-generation-batches-v945.md#v980-当前-linux-生成回归第-5-组)。 V930—V933 固定候选的本地 Linux 构建和发现通过，第 1 组原超时保留，原失败用例独立复验通过，Linux 六组仍待验。[Linux 独立回归](testing/current-linux-generation-v930-v931.md)。 2026-11-10 查询官方运行与作业 API，当前 [68af5319 的 GitHub CI](https://github.com/zhao-yc/metarill/actions/runs/42747715116) 15 个作业仍排队，执行器分配和已启动步骤均为零。较早的 [ffcd80d3 CI](https://github.com/zhao-yc/metarill/actions/runs/42703271783) 和 [1926a702 CI](https://github.com/zhao-yc/metarill/actions/runs/42737923651) 已取消。 86775982 的 [GitHub CI](https://github.com/zhao-yc/metarill/actions/runs/42637193256) 全部 15 项通过。后续为验收文档更新，最终开发提交 CI 仍须单独核对。[PR 检查](https://github.com/zhao-yc/metarill/pull/1/checks) | e682cd12 的 CI 15 项通过，独立 Linux 完整回归进行中 |
| 真实 Tiny Cloud 编辑 | e682cd12 两份正式 ZIP 的三个配置样例均为空，原选择器及配套许可已核对。[V1143 当前双包分发核对](testing/current-release-distribution-v963.md#v1143-当前双包分发与数据库边界)。 abbe89fb 的原预览、空配置提示与已有 HTML 字节保留已实测。[V1058 当前原富文本空配置](testing/fixed-empty-tiny-cloud-web-v839.md#v1058-当前候选原富文本空配置复验)。 abbe89fb 两份实际 ZIP 的三个示例 key 均为空，原选择器和分发许可已核对。[V1033 当前双包分发核对](testing/current-release-distribution-v963.md#v1033-当前双包分发与数据库能力边界)。 0b65780c 两份发行 ZIP 的三个示例 key 均为空，原选择器、许可与富文本封装核对通过。见 [V963 当前发行文件](testing/current-release-distribution-v963.md)。86775982 的原富文本空配置、原预览、内嵌过滤与保存后原字节保留通过。见 [V839 固定候选原富文本空配置](testing/fixed-empty-tiny-cloud-web-v839.md)。 | 待有效配置后验收实际 Cloud 编辑、中文资源和图片上传 |

较早候选 `5c8fcb82` 的同父明细 Excel 追加已通过 V603 正式门槛。四并发吞吐为 Java 的 95.8%、99.5%、99.7%，一轮 p95 触线，未构成稳定退化。该结果覆盖固定原八行工作簿及最小权限配置。同一 C# 候选的四读未构成稳定退化、七步写入未构成稳定退化。客户及合同 Excel 追加导入未构成稳定退化。V608 本地受保护下载及 V609 三页 Excel 导出分别未构成稳定退化、未构成稳定退化。V611 合同及明细的六组关系维护未构成稳定退化。V626 用相同候选复验 108 个固定原请求、1,313 条驱动命令及事务语义，见 [当前 SQL 与事务](testing/current-paired-sql-v626.md)。其余性能场景和整体签收继续核对。见 [文件、导出及维护复验](testing/paired-release-candidate-file-export-v608-v610.md)。见 [V601–V604 验收](testing/paired-child-negative-grant-cache-v604.md) 和 [V605–V607 复验](testing/paired-release-candidate-read-write-v605-v606.md)。

V616 以同一候选补齐原客户复制，8,624 次请求、六轮整场景达标。V617 以同一候选补齐合同和明细逐次修复的 208 次回放，同一表三轮中未达到稳定退化条件。逐轮比值、实库读回与资源核验见 [当前复制与逐次修复](testing/paired-release-candidate-copy-repair-v616-v617.md)。

历史候选的 V573、V579 和 V588 保留为退化和诊断证据。其结果对应各自固定版本。V600 确认最小权限账号走两阶段写入，V602 证实缩减的是重复权限探测。具备权限时的单次写入和故障回滚由 V604 独立核验。见 [V588 独占持续诊断](testing/paired-current-child-isolated-continuous-v588.md) 和 [V597–V600 运行路径](testing/paired-child-inline-cache-web-v600.md)。

### 当前性能签收范围

当前候选 `e682cd12` 的九类实测已收回，45,756 个正式请求、66 个配对轮次。首测的逐次修复曾达到稳定退化条件。[V1130 九类正式结果](testing/current-mysql-performance-v1130.md)。隔离诊断和同条件正式复验已完成，复验六轮均未触线，原正式退化留证。[V1139 逐次修复正式复验](testing/current-mysql-performance-v1130.md#v1139-逐次修复同条件正式复验)。[V1133 逐次修复分段诊断](testing/current-release-child-performance-v957-v958.md#v1133-当前候选逐次修复分段诊断)。

当前候选 `abbe89fb` 的九类实测已收回，45,756 个正式请求、66 个配对轮次。九类均通过稳定性门槛。[V1032 九类正式结果](testing/current-mysql-performance-v1032.md)。

当前候选 `abbe89fb` 已完成Excel 导出 2,592 个正式请求，未达到稳定退化条件。累计实测八类、37,356 个正式请求和 60 个配对轮次。[V1032 Excel 导出阶段](testing/current-release-child-performance-v957-v958.md#v1032-当前候选-excel-导出阶段)。

当前候选 `abbe89fb` 已完成受保护附件下载 4,800 个正式请求，未达到稳定退化条件。累计实测七类、34,764 个正式请求和 54 个配对轮次。[V1032 受保护附件下载阶段](testing/current-release-child-performance-v957-v958.md#v1032-当前候选受保护附件下载阶段)。

当前候选 `abbe89fb` 已完成父级 Excel 导入 864 个正式请求，未达到稳定退化条件。累计实测六类、29,964 个正式请求和 48 个配对轮次。[V1032 父级 Excel 导入阶段](testing/current-release-child-performance-v957-v958.md#v1032-当前候选父级-excel-导入阶段)。

当前候选 `abbe89fb` 已完成普通写入 8,400 个正式请求，未达到稳定退化条件。累计实测五类、29,100 个正式请求和 42 个配对轮次。[V1032 普通写入阶段](testing/current-release-child-performance-v957-v958.md#v1032-当前候选普通写入阶段)。

当前候选 `abbe89fb` 已通过只读查询 19,200 个正式请求，累计四类、20,700 个正式请求和 36 个配对轮次。[V1032 只读查询阶段](testing/current-release-child-performance-v957-v958.md#v1032-当前候选只读查询阶段)。

当前候选 `abbe89fb` 的逐次修复和整表维护已通过。连同同合同明细导入，共 3 类、1,500 个正式请求、30 个配对轮次。[V1032 维护场景](testing/current-release-child-performance-v957-v958.md#v1032-当前候选逐次修复与整表维护)。

V1031 使用 `abbe89fb` 完成同合同明细导入 1,200 个正式请求及六个配对轮次。该阶段通过同合同明细导入门槛，同一候选的九类结果见 V1032。[V1031 同合同明细](testing/current-release-child-performance-v957-v958.md#v1031-当前候选同合同明细导入)。

前一候选 `310a0ab3` 的九类实测已收回，45,756 个正式请求、66 个配对轮次。九类均通过稳定性门槛。[V1014 九类正式结果](testing/current-mysql-performance-v1014.md)。

前一候选 `310a0ab3` 已完成Excel 导出 2,592 个正式请求，未达到稳定退化条件。累计实测八类、37,356 个正式请求和 60 个配对轮次。[V1014 Excel 导出阶段](testing/current-release-child-performance-v957-v958.md#v1014-当前候选-excel-导出阶段)。

前一候选 `310a0ab3` 已完成受保护附件下载 4,800 个正式请求，未达到稳定退化条件。累计实测七类、34,764 个正式请求和 54 个配对轮次。[V1014 受保护附件下载阶段](testing/current-release-child-performance-v957-v958.md#v1014-当前候选受保护附件下载阶段)。

前一候选 `310a0ab3` 已完成父级 Excel 导入 864 个正式请求，未达到稳定退化条件。累计实测六类、29,964 个正式请求和 48 个配对轮次。[V1014 父级 Excel 导入阶段](testing/current-release-child-performance-v957-v958.md#v1014-当前候选父级-excel-导入阶段)。

前一候选 `310a0ab3` 已完成普通写入 8,400 个正式请求，未达到稳定退化条件。累计实测五类、29,100 个正式请求和 42 个配对轮次。[V1014 普通写入阶段](testing/current-release-child-performance-v957-v958.md#v1014-当前候选普通写入阶段)。

前一候选 `310a0ab3` 已通过只读查询 19,200 个正式请求，累计四类、20,700 个正式请求和 36 个配对轮次。[V1014 只读查询阶段](testing/current-release-child-performance-v957-v958.md#v1014-当前候选只读查询阶段)。

前一候选 `310a0ab3` 的逐次修复和整表维护已通过。连同同合同明细导入，共 3 类、1,500 个正式请求、30 个配对轮次。[V1014 维护场景](testing/current-release-child-performance-v957-v958.md#v1014-当前候选逐次修复与整表维护)。

前一候选 `310a0ab3` 已完成同合同明细导入 1,200 个正式请求及六个配对轮次。本轮通过同合同明细导入的性能门槛。其余八类仍需使用同一候选执行。[V1013 同合同明细](testing/current-release-child-performance-v957-v958.md#v1013-当前候选同合同明细导入)。

V634 复核当前产品 `5c8fcb82` 的九类固定负载。66 对正式轮次、45,756 个请求、7,182 对响应摘要和 264 个资源窗口全部复算，三个单轮触线均未达到各组的稳定退化条件。V626 的 108 个原请求及 1,313 条 SQL 命令作为单独的语义和事务证据，保留当时的过期摘要重放差异。V635 调整手写样板的两条 Web 入口。V636 已通过正式 MySQL 两入口、真实 Agent CLI、事务与文件核对。见 [样板正式复验](testing/sample-contract-web-mysql-v636.md)。V637 补同一合同的阻塞方提交及回滚，独立观察 MySQL 真实锁等待。见 [合同实库行锁](testing/sample-contract-lock-mysql-v637.md)。V634 固定矩阵达标，其余负载和总签收独立跟踪。见 [当前性能矩阵](testing/current-mysql-performance-matrix-v634.md)。 V645 使用 31cf5321 发行包中的 Data 程序集复验固定合同和明细逐次修复，144 个正式请求、6 轮均无触线。其余当前候选负载与总签收独立跟踪。见 [当前逐次修复性能](testing/current-repair-performance-v645.md)。

V646／V647 在同一当前基准候选复验四读及客户七步写入，共 27,600 个正式请求及 24 个资源窗口。四读 0 轮触线，两档均未构成稳定退化。七步写入 0 轮触线，两档均未构成稳定退化。两轮资源独立核清。见 [当前核心读写](testing/current-read-write-v646-v647.md)。

V649–V651 在同一当前候选复验客户及合同 Excel 追加、受保护附件下载和三页 Excel 导出，共 8,256 个正式请求。客户及合同 Excel 追加 0 轮触线，两档均未构成稳定退化。受保护附件下载 1 轮触线，两档均未构成稳定退化。三页 Excel 导出 0 轮触线，两档均未构成稳定退化。见 [当前导入、下载与导出](testing/current-import-file-export-v649-v651.md)。

V652–V654 补齐同父明细 Excel 追加、合同与明细六组维护和客户复制。同父明细 Excel 追加 2 轮触线，未构成稳定退化。合同与明细六组维护 0 轮触线，未构成稳定退化。客户复制 1 轮触线，未构成稳定退化。当前 31cf5321 的九类矩阵合计 66 对正式轮次、45,756 个正式请求。九类固定场景均未构成稳定退化。见 [当前固定矩阵](testing/current-fixed-performance-matrix-v652-v654.md)。

V655 补当前候选 108 个原请求的独立 SQL 观察。1,313 条驱动命令已核对，过期摘要重放两端均回滚并返回原 HTTP 200／R500。见 [当前 SQL 与回滚](testing/current-paired-sql-v655.md)。

V669 用 49d51ef5 候选的共享程序集复验四读和客户七步写入，27,600 个正式请求。客户及合同四读 0 轮触线，未构成稳定退化。客户七步写入 1 轮触线，未构成稳定退化。其余负载和最终签收继续。见 [V669 候选核心读写](testing/current-release-payload-read-write-v669.md)。

V670 在 49d51ef5 的共享程序集上复验同父明细 Excel 及客户复制。**同父明细 Excel 四并发三轮触线，构成稳定退化，阻断首版签收。** 客户复制 0 轮触线，未构成稳定退化。本机 Windows 29 组已通过，正式 CI 另验。见 [V670 候选明细与复制](testing/current-release-child-copy-v670.md)。

V671／V672 在同设备与持久化设置的独占 MySQL 复现四并发退化。V672 核对 12 个完整窗口、每端 4,928 条新增明细与资源回收。四并发首插至提交开始及 COMMIT 更长，作为下一步性能修复依据。正式发布门槛沿用 V670。见 [同父导入分段](testing/current-release-child-phases-v671-v672.md)。

V673 复算当前最小权限路径的逐批语句。四并发下，八次缓存 UPDATE 加上更新前锁读和明细回读平均 9.93～10.47 ms。此区间仍需保持触发器、写后核验与回滚语义。程序代码相同的 3e96b919 CI 全部 15 项通过，最终固定 SHA 和性能门槛继续独立验收。见 [缓存写入分解](testing/current-child-cache-write-decomposition-v673.md)。

V674／V675 验证合并回执候选。实库接口的触发器、行号及整批回滚通过。四并发 1/3 轮触线。候选两档均未达到稳定退化条件，保留该数据层改动。完整首版固定包、其余负载和最终 CI 继续独立核验。 见 [合并回执候选](testing/packed-receipt-candidate-v674-v675.md)。

V676–V678 固定 c2eddf71 的两份 Linux x64 候选包、三种实库安装及 Windows 29 组回归通过。发行包共享程序集的同父明细 Excel 四并发 1/3 轮触线。两个并发档均未构成稳定退化。最终 CI 和其它首版签收继续按本表核对。见 [固定候选包](testing/fixed-mysql-release-candidate-v676-v678.md)。

V679 固定同包核心四读与七步写入。客户及合同四读 0 轮触线，未构成稳定退化。客户七步写入 0 轮触线，未构成稳定退化。27,600 个正式请求、24 个资源窗口和独占资源回收均独立核对。见 [核心读写复验](testing/fixed-release-core-performance-v679.md)。

V680 同包复验两页导入、受保护下载及三页导出。客户及合同 Excel 追加 0 轮触线，未构成稳定退化。受保护附件下载 1 轮触线，未构成稳定退化。三页 Excel 导出 0 轮触线，未构成稳定退化。V682 补齐该包对应的平台与独立应用 H5 本地导出。实际浏览器验收继续单列。见 [固定导入、文件及 H5](testing/fixed-release-import-file-export-h5-v680-v682.md)。

V681 补齐同一固定候选的三组测试。合同明细逐次修复及客户复制单并发均有两轮 p95 触线，V08 仍阻断发布。六组关系维护无稳定退化，客户复制有稳定退化。本轮 8,700 个、九类固定场景合计 45,756 个正式请求。数据、索引和测试资源逐组独立核对，需定位尾部延迟后以原条件复验。见 [V681 性能复验](testing/fixed-release-maintenance-copy-v681.md)。

V683 在受控同源网关用固定包实测平台桌面登录、菜单和退出，以及独立应用移动 Web 普通只读、编辑 CRUD 和退出。MySQL 独立回读和资源清理通过。实际发行域名、公开证书和其余组合继续独立核验。见 [V683 固定包 Web](testing/fixed-release-browser-v683.md)。

V684 同包复验逐次修复。合同／明细分别有 1／1 轮触线。V681 原失败保留，复制七步已按原回执分段，性能继续阻断。见 [V684 复现性核对](testing/fixed-repair-repro-v684.md)。

固定产品 `c2eddf71` 的 [GitHub CI](https://github.com/zhao-yc/metarill/actions/runs/42491229657) 已通过全部 15 个作业，含 Linux／Windows 后端、12 个生成分组和前端。

V685 在同存储设备的独占 MySQL 对 230 个原写入请求做服务端分段。C# 明细和删除副本最慢样本的 COMMIT 分别约占 55% 和 80%。正式性能阻塞仍按 V681 记录。见 [V685 服务端分段](testing/fixed-write-server-phases-v685.md)。

V686 追加提交阶段采集。178 笔提交均匹配服务端 handler commit，C# 明细最慢样本中该阶段为 208.1 毫秒。具体等待原因与正式复验仍待。见 [V686 提交阶段](testing/fixed-commit-waits-v686.md)。

V688 关闭采集并按原门槛复验 8,544 个正式请求。明细逐次修复再次稳定退化，客户复制本轮未触发稳定退化，V681 正式失败保留。见 [V687／V688 诊断与复验](testing/fixed-write-formal-recheck-v687-v688.md)。

V689 核对 178 笔提交的同线程 binlog 同步以及独占实例 redo 计数。明细最慢 C# 样本中，同线程 binlog 同步约 66.9 毫秒，实例 redo 计数另列。见 [V689 binlog 与 redo 诊断](testing/fixed-redo-binlog-diagnostics-v689.md)。

V690 合并整表回填结构核验，真实 MySQL 物理对照确认驱动命令从 14 条降到 10 条，原锁、回滚及回退通过。待用新固定包复验正式性能。见 [V690 回填结构核验](testing/maintenance-schema-check-batch-v690.md)。

V691 固定 b99fdcec 的平台与独立 Full 已通过包读回、许可及三种真实 MySQL 安装。V692：逐次修复未触发稳定退化，合同／明细触线 0／1 轮。六组整表维护未触发稳定退化。其他性能及最终签收继续核对。见 [V691／V692 固定包与复验](testing/schema-batch-fixed-release-v691-v692.md)。

V693 用 b99fdcec 的固定 Full 复验客户复制，8,400 个正式请求，单／四并发分别触线 0／0 轮，未触发稳定退化。见 [V693 客户复制](testing/schema-batch-customer-copy-v693.md)。

V695 从 b99fdcec 的固定 Git 归档通过 Windows 29 组，九个后端套件 3382 项通过，219 项条件跳过，715 份生成与同步文件逐字一致。见 [V695 Windows 回归](testing/schema-batch-windows-v695.md)。

V694 用 b99fdcec 固定 Full 完成剩余六类负载，37,056 个正式请求，六组资源独立核清。合并同候选 V692／V693 已核对 45,756 个正式请求，存在稳定退化，继续阻断签收。见 [V694 当前固定性能矩阵](testing/schema-batch-performance-matrix-v694.md)。

V696 从 b99fdcec 的平台与 Full 固定 ZIP 重新导出 H5，并在受控同源网关实测平台桌面登录、菜单和退出，以及独立应用移动 Web 普通只读、编辑 CRUD 和退出。MySQL 独立回读和资源清理通过。实际发行域名、公开证书和其余组合继续独立核验。见 [V696 固定包 Web](testing/fixed-release-browser-v696.md)。

V697 以 b99fdcec 的固定公开 Full 实测六种跨表覆盖原桌面入口及无效级联末行整批拒绝。独立 MySQL 核对原主键、稀疏字段、对照记录和本轮资源。见 [V697 原 Web 覆盖](testing/current-cross-overwrite-web-v697.md)。

V698 恢复原嵌入子表列表页编辑。普通编辑、只读、父范围和实际失焦保存已通过固定应用的桌面 Web 与独立 MySQL。见 [V698 子表列表编辑](testing/embedded-child-inline-edit-v698.md)。

V699 验证同父明细追加复用已锁目标的单文件候选，四并发仍有两轮触线。候选未合入，性能门槛继续阻断。见 [V699 复用已锁目标](testing/packed-locked-target-v699.md)。

V700 将子表列表编辑修复固定进开发提交 2855fc5b 的 Linux 平台候选。两个候选 ZIP 已独立读回，普通 Full 源码不变，子表样例重新生成后与 V698 已实库验收的 680 份源码一致。见 [V700 固定 Linux 候选](testing/fixed-linux-child-inline-v700.md)。

V701 用 V694 原镜像完成 12 个服务端阶段窗口的独立复算。四并发差距在首次插入到提交开始已经出现，优先处理持锁阶段。正式性能门槛继续阻断。见 [V701 当前同父明细分段](testing/current-child-import-phases-v701.md)。

V702 合并插入计数与锁读的候选通过实库功能，四并发三轮触线，未合入。正式性能门槛继续阻断。见 [V702 合并插入回执](testing/packed-insert-receipt-v702.md)。

V703 已配置的共享锁证明可在本事务复用。四个同父并发请求通过真实生成接口，后七行随 INSERT 写缓存；默认配置的正式性能门槛仍待。见 [V703 部署证明复用](testing/inline-cache-shared-proof-v703.md)。

V705 验证默认同父追加合并双表物理预检的单文件候选，本轮两档均未达到稳定退化条件。整包仍待核对。见 [V705 合并追加物理预检](testing/combined-append-layout-v705.md)。

V706 集成包已严格构建并独立再生成。V707 用同一固定产品 cfdfb2fe 复验默认同父追加，1,200 次正式请求和六轮均达标。完整性能矩阵待核对。见 [V707 集成包默认同父复验](testing/current-integrated-child-append-v707.md)。

V709 从 cfdfb2fe 的固定 Git 归档通过 Windows 29 组，九个后端套件 3382 项通过，219 项条件跳过，715 份生成与同步文件逐字一致。见 [V709 Windows 回归](testing/integrated-windows-v709.md)。

V708 以 cfdfb2fe 的同一固定包完成 45,756 个正式请求、66 个配对轮次。九类固定负载均未达到稳定退化条件。见 [V708 集成性能矩阵](testing/integrated-mysql-performance-v708.md)。

V710 用 cfdfb2fe 的平台与 Full 固定包及逐字核对的原 H5，在受控同源网关实测平台桌面登录、菜单和退出，以及独立应用移动 Web 普通只读、编辑 CRUD 和退出。MySQL 独立回读和资源清理通过。实际发行域名、公开证书和其余组合继续独立核验。见 [V710 集成包 Web](testing/integrated-web-v710.md)。

V711 补同一候选的平台移动 Web 配置列表与独立 Full 桌面普通角色的只读和 CRUD。合并 V710，平台和独立应用均有两种 Web 视口实测。三次会话注销和数据结果已独立核对。实际发行域名及公开证书按部署环境验收。见 [V711 集成包交叉视口 Web](testing/integrated-web-cross-viewports-v711.md)。

V712 用同一固定候选验证平台与独立 Full 的 Caddy TLS 1.3、正确主机与错误证书、原静态及真实登录和生成 CRUD。公开发行环境与浏览器直连另验。见 [V712 固定候选 HTTPS](testing/integrated-caddy-tls-v712.md)。

V713 以同一候选实测原配置页、独立 Full 桌面 XLSX 回滚与实际下载、移动只读及独立 MySQL。见 [V713 固定候选原 Web 与 Excel](testing/integrated-original-web-excel-v713.md)。

V714 从固定候选源码完成真实 MySQL 同库和分库的文件归属、删除与对象重试、关系传播和稀疏级联回滚，18 项全过。见 [V714 固定候选文件事务](testing/integrated-file-transactions-v714.md)。

V715／V716 以固定候选复验分库与共库 S3 的原路由、普通权限、对象回滚与删除，共库补同址重启。见 [V715／V716 固定候选平台 S3](testing/integrated-platform-s3-v715-v716.md)。

V717 固定候选用公开工厂生成完整管理包，完成正式 Web、真实 OAuth CLI、MCP 撤权、账号初始化和重启。见 [V717 固定候选完整管理包](testing/integrated-management-package-v717.md)。

V718 固定候选补在线助手双项目真实写入、仅后端同步及同地址重启后的原批次恢复。见 [V718 固定候选在线助手](testing/integrated-device-runtime-v718.md)。

V719 固定候选从原 Web 实测在线助手三批六任务及回执丢失后的只读恢复。见 [V719 原 Web 批量恢复](testing/integrated-device-browser-v719.md)。

V728 提供仓库内可执行的发行构建与校验命令，固定 cfdfb2fe 的对应 ZIP 在不同目录两次重建逐字一致。三种隔离安装以新 ZIP 复验通过。同一产品的 GitHub CI 全部 15 项通过，含两个后端、12 个生成分组和前端。交付提交的 CI 继续单列。见 [V728 公开发行构建与安装](testing/public-release-build-v728.md)。

V729 从原生成详情复验 Tiny Cloud 空配置、预览及原 HTML 字节保留，并修复内嵌 HTML 事件属性执行。独立 Full 的真实浏览器、实库和既有项目更新均已核对。实际 Cloud 编辑与发行域名仍待验收。见 [V729 富文本 Web](testing/empty-tiny-cloud-rich-text-v729.md)。

V730 固定发行工具的程序集来源标记，历史包从 Git 仓库内外重建逐字一致。含富文本修正的 c7491f41 候选两包通过三种安装，实际平台按原接口配置富文本并下载 Full，返回的原 ZIP 严格编译及前端闭包通过。见 [V730 发行来源与富文本生成](testing/release-source-revision-and-rich-text-v730.md)。

V731 补齐空库字段布局默认值。分库和共库均用原接口验证省略布局可保存，重复初始化保留手工布局和原密码。见 [V731 空库字段布局](testing/platform-seed-layout-defaults-v731.md)。

V732 使用 de6da757 的平台与独立 Full 原始 ZIP，在同一轮通过三种 Production 安装及原 HTTP。两个包的 18 个项目程序集均指向候选提交。见 [V732 固定候选安装](testing/exact-release-install-v732.md)。

V733 固定 de6da757 的 Windows 本机回归通过，覆盖九个后端套件、SQL 观察器、定向生成、原前端与 Agent CLI，717 份再生成产物逐份一致。见 [V733 Windows 本机回归](testing/exact-release-windows-v733.md)。

V734 从 V732 的两个 ZIP 导出平台与独立应用 Web 产物，校验首页入口和全部导出文件。见 [V734 固定候选 Web](testing/exact-release-web-build-v734.md)。

V735 用同一 de6da757 候选的宿主和 H5，实测平台管理端与独立应用各自的桌面和移动 Web。普通只读及编辑角色、合成 CRUD、六次注销和独立 MySQL 读回通过。见 [V735 固定候选浏览器](testing/exact-release-browser-v735.md)。

V736 固定 de6da757 的同父明细 Excel 追加通过，1,200 个正式请求，四并发一轮吞吐触线且未构成稳定退化。见 [V736 同父明细追加](testing/exact-mysql-child-append-v736.md)。

V743 固定 de6da757 的合同明细逐次修复出现三轮两轮触线，当前性能签收阻断。见 [V743 性能阻断](testing/exact-mysql-repair-regression-v743.md)。

V737 以 de6da757 的同一固定包完成 45,756 个正式请求、66 个配对轮次。八类负载未构成稳定退化。合同明细逐次修复三轮中两轮触线，继续阻断性能签收。见 [V737 集成性能矩阵](testing/exact-mysql-performance-v737.md)。

V744 在独占 MySQL 完成同一候选的 230 个分段诊断请求，核对提交和读取开销，正式矩阵阻断保留。见 [V744 分段诊断](testing/exact-mysql-repair-phases-v744.md)。

V738 从固定候选源码完成真实 MySQL 同库和分库的文件归属、删除与对象重试、关系传播和稀疏级联回滚，18 项全过。见 [V738 固定候选文件事务](testing/exact-file-transactions-v738.md)。

V740／V741 以固定候选复验分库与共库 S3 的原路由、普通权限、对象回滚与删除，共库补同址重启。见 [V740／V741 固定候选平台 S3](testing/exact-platform-s3-v740-v741.md)。

V745 短文本叶子维护候选已通过实库锁读、稀疏末端旧值和回滚用例。后续固定镜像复验见 V747。见 [V745 叶子维护窗口](testing/leaf-maintenance-clean-window-v745.md)。

V747 固定候选完成逐次修复和整表维护同条件复验。两组均达到原稳定性门槛。

V750 已用公开工具构建并校验 4ef3ca76 的平台与独立 Full ZIP，固定前端输入逐字一致。实际安装和浏览器证据另验。见 [V750 公开工具固定包](testing/leaf-public-release-build-v750.md)。

V748 以 4ef3ca76 的同一固定包完成 45,756 个正式请求、66 个配对轮次。九类固定负载均未构成稳定退化，性能矩阵达到原门槛。见 [V748 集成性能矩阵](testing/exact-mysql-performance-v748.md)。

V756 使用 4ef3ca76 的平台与独立 Full 原始 ZIP，在同一轮通过三种 Production 安装及原 HTTP。两个包的 18 个项目程序集均指向候选提交。见 [V756 固定候选安装](testing/leaf-public-release-install-v756.md)。

V752 用 4ef3ca76 公开 ZIP 宿主和同源 H5，实测平台管理端与独立应用各自的桌面和移动 Web。普通只读及编辑角色、合成 CRUD、六次注销和独立 MySQL 读回通过。见 [V752 固定候选浏览器](testing/leaf-public-release-browser-v752.md)。

### 首版签收核对

| 项目 | 已核对的证据 | 待核对 |
| --- | --- | --- |
| 安装、空库及生成 | e682cd12 平台及独立 Full 在严格 MySQL TLS 下完成证书拒绝、三库安装和原业务接口。[V1150 当前候选严格 MySQL TLS](testing/mysql-strict-tls-web-v565.md#v1150-当前发行候选严格-mysql-tls)。 abbe89fb 平台及独立 Full 在严格 MySQL TLS 下完成证书拒绝、三库安装和原业务接口。[V1080 当前候选严格 MySQL TLS](testing/mysql-strict-tls-web-v565.md#v1080-当前发行候选严格-mysql-tls)。 310a0ab3 的两包通过三拓扑空库安装和重启。[V1009／V1010 发行候选](testing/storage-mode-linux-release-v1009-v1010.md)。 0b65780c 的两包通过分库、共库和独立 Full 的空库安装、权限、CRUD 及重启。见 [V944 当前三拓扑](testing/text-projection-runtime-topologies-v944.md)。 V721 的 cfdfb2fe 固定平台及 Full ZIP 通过三种 Production 隔离安装、普通启动零 DDL、反查、重复保护、原 HTTP 和重启。见 [V721 固定包安装](testing/integrated-linux-install-v721.md)。V725 用同一候选在原 Web 核对预览、取消、旧计划拒绝和重新预览后单次执行。见 [V725 原 Web 结构同步](testing/integrated-schema-sync-web-v725.md)。V726 从平台正式接口新增配置并下载公开 Full，用返回的同一 ZIP 严格发布和独立安装，核对原登录、新配置表 CRUD 与实库。见 [V726 生成到独立安装](testing/integrated-platform-download-install-v726.md)。V727 从同一 ZIP 构建原前端，以普通编辑／只读账号复验新表桌面增改删、移动回看和实库。见 [V727 原下载包 Web](testing/integrated-downloaded-package-web-v727.md)。V722 在同一固定源码的 Windows 宿主复验跨实例分库及本地附件恢复、活动会话撤销与计数不符回滚。见 [V722 固定候选恢复](testing/integrated-cross-instance-recovery-v722.md)。此前 Linux 候选实际通过平台分库、共库和独立 Full 的 Production 空库安装、重复保护、原 HTTP CRUD、会话隔离与重启。独立 Full 两次生成的 681 个文件逐份一致。见 [当前 Linux 空库](testing/current-linux-candidate-runtime-v613.md)、[严格 MySQL TLS](testing/mysql-strict-tls-web-v565.md)、[再生成](testing/mysql-release-rebuild-v463.md)。 V630 补平台分库 MySQL 和本地附件隔离恢复，目标真实登录、授权与文件字节读回通过。见 [分库恢复演练](testing/current-split-backup-restore-v630.md)。 V631 补分库 MySQL 与两套独占 S3 兼容服务恢复，原逻辑地址接入目标后原权限与附件字节通过。见 [分库 S3 恢复演练](testing/current-split-s3-backup-restore-v631.md)。 V632 补两个不同 MySQL 8.4.11 实例之间恢复平台分库与本地附件。见 [跨实例演练](testing/current-cross-instance-backup-restore-v632.md)。 V633 补快照中活动 Web、Agent access 及 refresh 的恢复实测，目标离线撤销及重新签发通过。见 [会话处置](testing/current-restored-session-invalidation-v633.md)。V638 从当前固定开发提交生成两个可校验 Linux x64 ZIP，从解包根再生成 Full 并通过三种隔离安装。见 [发行包与空库安装](testing/fixed-linux-release-archives-v638.md)。V642 修正普通结构同步的同名索引差异判断，已在 MySQL 8.4.11 对照旧、新实现，并核对平台与 Full 包编译。见 [索引物理差异](testing/mysql-index-physical-drift-v642.md)。V643 用含该修复的 31cf5321 两份实际 ZIP 复验三种 Production 安装、只读反查、原 HTTP CRUD 和重启。见 [当前 Linux 候选](testing/current-linux-archives-v643.md)。V658 用同一候选从原表详情按钮验证结构预览、取消、一次确认、执行记录及零差异，独立 MySQL 核对物理索引。见 [原 Web 结构同步](testing/current-original-web-schema-v658.md)。V659 补分库同名的复合降序索引反查、审核后漂移拒绝、正式配置保存与正向零差异。见 [当前降序索引](testing/current-descending-index-import-v659.md)。V668 固定 49d51ef5 的两份 ZIP，通过三种安装、反查和 CRUD。见 [V668 Linux x64 候选](testing/current-mysql-release-packages-v668.md)。 | 核对首版固定源码的安装、生成与同版恢复记录；实际部署配置、跨版本迁移及新增对象存储供应商按对应后续事项验收。 |
| 桌面与移动 Web | abbe89fb 平台实际 ZIP 已完成原 H5 导出，156 份产物与 V1040 双视口实测产物逐件核对。[V1122 当前平台原 Web 构建](testing/current-release-distribution-v963.md#v1122-当前平台原-web-发行构建)。 abbe89fb 的原分库管理端及独立 Full 普通只读、编辑均通过双视口，六次退出及 MySQL 读回已核实。[V1040 当前双视口 Web](testing/current-exact-release-web-v913.md#v1040-当前发行候选原-web-双视口)。 abbe89fb 的独立 Full 原 H5 已实际导出并核对登录、客户列表和详情分包。[V1035 当前独立 Full Web 构建](testing/current-release-distribution-v963.md#v1035-当前独立-full-原-web-发行构建)。 310a0ab3 的原分库管理端和独立 Full 普通只读、编辑均通过双视口。六次退出及 MySQL 读回已核实。[V1011 当前双视口 Web](testing/current-exact-release-web-v913.md#v1011-当前发行候选原-web-双视口)。 1926a702 已通过原分库管理端和独立 Full 普通只读、编辑的桌面及移动 Web。见 [V889 当前候选双视口 Web](testing/current-release-web-v889.md)。 ffcd80d3 已复验原分库管理端、独立 Full 普通只读和编辑在桌面及移动 Web 的完整固定流程。见 [V884 当前候选双视口 Web](testing/current-release-web-v884.md)。 当前 Linux 候选已通过独占 Caddy 同源部署、客户端地址审计与原业务。平台原桌面浏览器通过登录、菜单和注销，独立 Full 原移动浏览器以普通角色完成客户 CRUD、退出和 MySQL 读回。另有原桌面与移动直连 HTTPS 证据。见 [Caddy 同源](testing/current-caddy-release-v627.md)、[原桌面](testing/current-caddy-https-v628.md)、[独立 Full 原移动](testing/current-full-caddy-mobile-v629.md)、[浏览器直连](testing/current-browser-direct-https-v614-v615.md)。V644 用 31cf5321 Linux 候选及重新导出的原 H5，验证桌面工作区和移动 Full 普通只读、编辑账号，三次注销及持久化经独立 SQL 核对。见 [当前候选 Web](testing/current-release-web-v644.md)。V657 复验原平台表配置、独立应用的桌面 Excel 导入与回滚、实际下载和移动只读回看。见 [当前原 Web 与 Excel](testing/current-original-web-excel-v656-v657.md)。V665 补原详情布局桌面分组、版本冲突与停用归属读回。见 [原详情布局](testing/current-detail-layout-web-v664-v665.md)。V666 补固定生成详情两种布局的普通账号保存重开、双视口与独立 SQL 验证。见 [V666 生成详情双视口](testing/current-generated-detail-layout-web-v666.md)。V667 补原移动工作台及正式 MCP 版本冲突，见 [V667 移动工作台与正式 MCP](testing/current-mobile-detail-layout-mcp-v667.md)。 V720 用当前固定候选复验原合同 5,014 候选的桌面分页、搜索、筛选、保存及移动回读。见 [原合同大候选](testing/integrated-contract-options-browser-v720.md)。 | 真实 Tiny Cloud 编辑按用户要求待有效配置，原移动裁切按 V01／V02 待确认。实际发行域名及公开证书链列入上线前环境检查。 |
| 关系、事务、文件及 Excel | V723 固定当前公开 Full 的六个 R0 模型，分别通过追加、覆盖、传播维护、并发锁及文件事务回滚。见 [V723 六模型跨表 Excel](testing/integrated-public-r0-cross-excel-v723.md)。V724 补同一候选原桌面六组覆盖选档和六组末行无效整批拒绝。见 [V724 原 Web 六模型覆盖](testing/integrated-cross-overwrite-web-v724.md)。单步 S3 回执丢失已复验精确归属清理。当前候选分库和共库文件事务 18 项通过。分库 Production 原 HTTP 的普通角色、实际下载、归属和失败回滚在 [V619／V620](testing/current-platform-files-v619-v620.md) 核对。[V621／V622](testing/current-platform-s3-v621-v622.md) 补同一候选分库与共库 S3 真实 HTTP、对象读回和共库会话重启。V640 补分库本地文件绑定、删除的两种并发顺序及平台撤权后原会话的读取拒绝。见 [分库文件竞态](testing/platform-split-file-race-v640.md)。V641 补同一 S3 竞态的对象字节、404 和空桶回收。见 [分库 S3 竞态](testing/platform-split-s3-file-race-v641.md)。见 [六模型覆盖](testing/current-standalone-and-public-r0-v521-v522.md)、[文件回滚](testing/legacy-s3-ambiguous-rollback-v556.md)。V661 用当前公开 Full 补六模型原桌面选档、同主键覆盖及无效级联末行整批拒绝，独立核对隔离。见 [当前原 Web 覆盖](testing/current-cross-overwrite-web-v660-v661.md)。V663 修复原添加联接字段入口，实测原桌面勾选、取消、保存及配置回读。见 [原联接工作台](testing/current-join-workbench-web-v662-v663.md)。 | 按 V03／V04 完成其余必要文件、角色及关系矩阵，再核对最终候选。 |
| 原助手、CLI 和 MCP | abbe89fb 的共库、分库及公开独立 Full 已通过真实 MCP，合计 126 个 HTTP 请求。[V1078 当前共库 MCP](testing/current-platform-full-mcp-v937-v938.md#v1078-当前候选共库-mcp-项目隔离)。 abbe89fb 的完整管理包同包验证真实 CLI、MCP、授权隔离和账号初始化。[V1048 当前完整管理包](testing/fixed-management-native-v840.md#v1048-当前发行源码完整管理包正式通过)。 abbe89fb 已通过平台分库和独立 Full 的 76 个真实 MCP 请求与撤权。[V1046–V1047 当前候选 MCP](testing/current-platform-full-mcp-v937-v938.md#v1046v1047-当前候选-mcp-实库权限复验)。 68af5319 平台分库和独立 Full 的 76 个真实 MCP 请求及撤权通过。见 [V937／V938 实库复验](testing/current-platform-full-mcp-v937-v938.md)。 C85、C88 已分别验证固定管理包的 CLI／MCP 及原浏览器双项目批量运行时。V623 用当前候选复验独立完整管理包、普通权限、真实 CLI／MCP 撤权及账号初始化。V624 补当前平台在线助手双工程写入和同地址重启。V625 用当前原 H5 实际验证部分失败、完整成功和回执丢失后只读恢复。见 [当前原 Web](testing/current-device-browser-v625.md)、[当前在线助手](testing/current-device-runtime-v624.md)、[当前管理包](testing/current-management-package-v623.md)、[设备批量浏览器记录](testing/device-runtime-browser-native.md)。 | 按 C 各项保留待验的宿主与权限组合，最终版本核对同包来源。 |
| 最终回归与交付 | e682cd12 的 Windows 固定源码 30 组回归通过。[V1131 当前 Windows 回归](testing/current-windows-regression-v946-v947.md#v1131-当前候选-windows-固定回归)。 abbe89fb 的 Windows 固定源码 30 组回归通过。[V1029 当前 Windows 回归](testing/current-windows-regression-v946-v947.md#v1029-当前候选-windows-固定回归)。 310a0ab3 的 Windows 固定源码 30 组回归通过。[V1015 当前 Windows 回归](testing/current-windows-regression-v946-v947.md#v1015-当前候选-windows-固定回归)。 0b65780c 的 Windows 30 组回归已通过，当前 Linux 和 CI 继续验收。见 [V946／V947 回归记录](testing/current-windows-regression-v946-v947.md)。 V648 固定 31cf5321 的 Windows 九个后端套件、SQL 观察器、定向生成、原前端和 Agent CLI 通过，715 份再生成产物一致。见 [当前 Windows 回归](testing/current-windows-candidate-v648.md)。已复核 [60b56aa2 的 CI](https://github.com/zhao-yc/metarill/actions/runs/42465548661) 全部 15 个作业成功，与固定产品 31cf5321 相比仅有文档差异。发布提交自身仍须核对。较早的 ff1a3858 已有 [15 个 CI 作业全部通过](https://github.com/zhao-yc/metarill/actions/runs/42460393575)，该提交早于 31cf5321 的结构修复。V639 固定当前源码在 Windows 通过九个后端套件、定向生成、前端及 CLI 回归，715 份生成及同步产物逐字一致。完整 CI 当时等待。见 [Windows 固定回归](testing/windows-candidate-regression-v639.md)。历史 V10 有 15 个绿色 CI 作业。V613 提交的 push 运行已取消，作业数为 0，同 SHA 的 PR 运行在 V615 观测时仍在等待。见 [PR 运行](https://github.com/zhao-yc/metarill/actions/runs/42435830859)。 | e682cd12 的 CI 与 Windows 固定源码回归通过。待两端完整生成器回归及 V11 最终签收。 |


2026-11-02新增证据：B123固定公开Full六模型的真实Web／MySQL／原XLSX／传播维护／文件回滚／重启通过，测试资源独立核清；A30首轮在数值反查阶段失败且清理结束，夹具修正后仍需正式复验。它们分别更新进度表，不将固定子集通过扩展为首版完成。

A31后续正式验收已通过实际默认与反查、普通真实身份；首笔生成新增响应的double位型错误使整条失败，资源已独立核清。A32修正独立宿主JSON模板登记并验证HTTP／Full包；完整数值链仍待新固定正式复验，不能以本地修复代替通过。

A35后续按固定0ba9330完成真实MySQL三表数值链：默认／反查、77位型矩阵、生成CRUD／派生筛选／统计、双页原XLSX／SheetJS／再导入、真实回滚／撤权／重启正式通过，资源独立核清。仅关闭该固定数值子集，MySQL首版总验收仍未完成。

C104后续固定da137bb完成三表TIME正式1过／0败／0跳过：14表显式安装／真实反查零Issues、四TIME持久化／原Web CRUD／SESSION模式回滚恢复／原XLSX／重启通过，唯一库／账号／合成存储及凭据独立清理核零。该项使用初始化所有者，仅关闭固定TIME子集，不替代普通权限矩阵、跨端或性能；MySQL首版总验收仍未完成。

2026-11-02 V37：固定7cc3a77／当前C#63f60dc的原双端显式初始化、三普通身份真实form登录／原页查询拒绝及前后资源接线已正式通过，资源／秘密新SSH独立核清。详见[固定范围与正式结果](paired-native-literal-fix-plan.md)。这只关闭初始化／身份前置子集，同业务数据、实际SQL参数、代表性业务／文件／Excel／传播维护、负载采样及跨端与MySQL首版总验收仍未完成，未运行性能负载。


2026-11-03 V49单轮复验已完成原六初始化／普通真实登录和权限；Java子页响应断言错误及32份不完整采集使整条失败，资源和秘密已新SSH核零。V50按原VO修正，V51元数据及真正Boot Ready离线修正通过，新镜像／完整正式业务及有效SQL仍待，详见[正式复验与边界](paired-observed-native-second-run.md)。MySQL首版不标完成。

2026-11-03 V52正式单轮：Java全37业务完成，C#分页计数协议失败；67份私有SQL记录严格完整性有效，双端覆盖／SQL语义／性能仍未完成。所有本轮远端资源和源秘密已独立核零；计数修复与重新打包另见[V52／V53记录](paired-observed-v52-counter-contract.md)，不计首版验收通过。

2026-11-03后续V56：工具12adbc3／产品38729a7与新C#e31823d已完成双方原六初始化、真实普通身份、各37业务及92记录结构核验，116私有证据回收，全部同轮资源／源秘密独立核零、key备份匹配。仅关闭当前同模型业务与采集结构子集，SQL语义／目标绑定／性能及首版总验收继续。见[V56正式结果](paired-observed-counter-result.md)。

2026-11-03后续V57／V58／V59：原失败日志及成功登录审计、用户名规则已接回，平台／完整包／管理来源／原助手更新闭包本机通过，原页面不改。247／159／32／141及最终CLI1分别留证且有重叠；新产品正式实库、原失败锁／Cap、最终提交CI、SQL语义、跨端及性能仍待。不能把这些本机结果替代MySQL首版总验收，详见[登录升级专项](login-failure-audit-compatibility.md)。

2026-11-03后续V61—V66：原Cap脚本与服务端校验、共享Redis失败锁及许可发布闭包已恢复，真实客户端子集与新Linux镜像已核。V66固定35353fe／cce17b6／8dbbcfc，原双端92请求及采集结构通过，错误审计与三成功登录事务独立证明，116私有证据回收、key备份匹配，全部本轮资源与源秘密独立核零。只关闭固定正式业务和登录审计子集；完整锁定窗口／重启、真实Cap／跨端、跨语言SQL语义／物理目标、代表性负载和最终CI仍待。见[V66正式结果](paired-login-protection-native-result.md)。


2026-11-04 V162：产品45b749a、共同关联索引的原客户七步写入已正式执行8624请求并独立重算，单并发／并发四各三轮均未触发退化。原V150缺索引模型的稳定退化及边界等待保留；采用相同索引须通过项目元数据保存和显式同步。28证据逐摘要核对，双库／账号／15容器／网络、源临时凭据及数据副本新连接核清。这里只关闭固定客户CRUD写入子集；当前候选读复验、SQL语义／计数闭环、文件／导入／维护等代表性负载、最终CI与跨端及MySQL首版总验收继续。详见[原始条件、六轮与清理](performance-acceptance.md#v162-共同关联索引后的原客户写入正式复测)。

2026-11-04 V164：当前完整候选同索引四读已正式执行19336请求，29证据和前后数据／实际索引独立复核，双库／账号／15容器／网络及源秘密数据新连接核清。两档各第三轮退化，未达原三轮稳定条件；波动尚未归因，不能标全部轮次或整体性能通过。文件／导入／传播维护负载、SQL语义／数量签收、最终资源／CI／跨端仍待，首版总验收继续。详见[完整六轮及边界](performance-acceptance.md#v164-当前完整候选同索引四读正式结果)。


2026-11-05 V186—V192：固定当前C#候选18310cc／ae626b79已完成原双端业务和附件／Excel／改名／C#回填，并补齐非空附件Excel导入、真实原子页再导入到新父及末行业务拒绝记录／文件引用整批回滚。157真实业务响应、十二原工作簿及221证据独立核对，全部本轮合成资源新连接核清；原业务提示误报内部500已由生成器修复，旧V188失败保留。仅关闭该固定功能子集，原Java自动缓存维护缺口明确保留；代表性性能、SQL语义／计数闭环、最终固定CI与真实跨端及首版总验收继续。详见[V192正式范围](performance-acceptance.md#v192-当前完整镜像的附件父子excel与末行业务规则正式通过)。


2026-11-05 V196：当前完整候选受保护文件首测完成4834下载／316801024完整校验字节、单并发与并发四各三轮均未触发原退化阈值，49证据和原10752行、资源回收独立核对。但文件基础两表的原Java／C#索引设计不同，已按真实DDL明确留证，不能算最终同索引性能签收；共同文件索引、实际SQL、其余代表性负载及V08继续。详见[六轮数据与实验边界](performance-acceptance.md#v196-受保护文件首测的六轮结果与文件基础索引差异)。

2026-11-07 V460。默认排他配置的固定合同明细导入完成 Java／C# 同条件复测，1,200 次计时、两个并发档六轮均未触线，独立核对每端 4,928 条新增明细及资源回收。固定负载达标，首版总验收及当前最终源码的整体证据继续。 [V460 正式复测](testing/paired-child-default-projected-layout-timing-v460.md)

2026-11-07 V466。当前候选 430a1e0c 同索引复测客户分页、计数、合同详情和合同明细分页，共 19,336 个请求。单并发和四并发各三轮均未触线，数据、索引及清理独立核验。该组只读负载已验收，其它代表性负载和最终固定提交的 CI 继续跟踪。[V466 正式复测](testing/paired-current-indexed-read-v466.md)

2026-11-07 V473。原后台账号初始化两种 MySQL 拓扑正式通过。原两个 HTTP 入口实测保留账号 ID 和档案、重置密码和角色并撤销旧 Web 会话；共库通过真实新密码登录，分库独立回读业务库。浏览器点击、分库独立登录、并发及 Agent 刷新授权规则仍列在 C07。 [V473 正式回执](testing/platform-admin-initialization-v473.md)

2026-11-07 V474。原后台初始化共库和分库均验证了末段会话写入触发实际 MySQL 约束错误后的整笔回滚。解除约束后原请求可重试，正式两项通过，独占资源已核清。 [V474 回滚验收](testing/platform-admin-initialization-rollback-v474.md)

2026-11-07 V475。共库账号重置与原真实登录通过两个并发顺序。重置先行时拒绝旧密码，登录先行时撤销刚签发的会话。分库原初始化和末段回滚回归通过。 [V475 并发验收](testing/platform-admin-login-race-v475.md)

2026-11-07 V476。原平台按钮初始化后，分库独立应用真实登录、菜单与权限通过。两个宿主在真实项目锁上的两种登录／重置顺序均通过，旧密码和已撤销会话按原协议拒绝。共库并发及双拓扑故障回滚复验通过。 [V476 双宿主验收](testing/platform-split-standalone-login-v476.md)

2026-11-07 V477。分库原桌面浏览器验证后台初始化取消零请求、确认一次，以及独立业务旧会话／旧密码拒绝、新密码登录和菜单。实库独立核对账号、角色和会话，两轮测试资源已核清。 [V477 浏览器验收](testing/platform-admin-initialization-browser-v477.md)

2026-11-07 V478。分库原移动 Web 在 390×844 通过后台详情「更多」菜单初始化。取消零请求、确认一次，独立业务旧会话和旧密码拒绝，新密码登录和客户菜单通过。两端原移动注销、数据库独立核验及资源回收通过。 [V478 移动 Web 验收](testing/platform-admin-initialization-mobile-v478.md)

2026-11-07 V479。真实 OAuth 验证现有有效 Agent 刷新授权门禁。授权撤销后，后台初始化可重试，保留刷新防重放记录。双拓扑实库复验通过，有效授权最终重置策略仍由 C07 跟踪。 [V479 Agent 防重放验收](testing/platform-admin-agent-replay-v479.md)

2026-11-07 V480。当前候选 1da7a305 以相同样本、关系索引、并发及资源复测客户、合同和明细原 Excel 导出。2,784 次回放和 1,392 次下载校验，单并发和四并发各三轮均达标。工作簿、时序与资源回收已独立核验，首版总验收继续。 [V480 正式复测](testing/paired-current-excel-export-v480.md)

2026-11-07 V481。当前候选 1da7a305 同条件复测原合同和合同明细的关系维护。8 次预检、208 次回放及每次 132 个旧值的物理修复和重复维护读回均通过。六组均达到固定维护场景的性能门槛，回收已独立核验。 [V481 正式复测](testing/paired-current-relation-maintenance-v481.md)

2026-11-07 V482。客户与合同追加导入。64 次预热和 864 次正式请求通过业务与资源检查。每端客户和合同各新增 1,856 行，合同明细表无本轮越界写入。六轮均达标。 [复测证据](testing/paired-current-excel-import-v482-v483.md)

2026-11-07 V483。合同明细同父追加导入。32 次预热和 1,200 次正式请求通过业务与资源检查。每端新增 4,928 条归属指定父合同的明细，逐值核对原父键及相关缓存。六轮中 1 轮触线，未触发稳定退化。 [复测证据](testing/paired-current-excel-import-v482-v483.md)

2026-11-07 V484。原 Java V8 与当前生成布局的两条移动固定底栏叠放已定位。H5 重建后在三个移动视口复现，原「子表上面」选项恢复完整点击范围。固定底栏模式的首版处理待确认。 [V484 诊断与布局复核](testing/mobile-embedded-footer-layout-v484.md)

2026-11-07 V485。当前候选 1da7a305 完成客户七步 8,624 次同条件回放，逐值核对每端 616 条墓碑及实际共同索引。单并发和四并发各三轮均达标。 [正式复测](testing/paired-current-customer-write-v485.md)

2026-11-07 V486。独立审阅原合同及明细 18 个请求的实际绑定参数和事务结局，含两端写后回滚。该范围计入 V17 固定样本。 [合同写入复核](testing/paired-contract-write-sql-v486.md)

2026-11-07 V495。补齐域名匹配的 VerifyFull、TLS1.2／1.3、双向证书与平台适配器／FreeSql 的真实拒绝及回滚。独占 MySQL 容器验收 8 项通过，五轮测试资源均独立核清。仅关闭该固定部署链，Web、最终性能／CI 和首版总验收继续。[V495 正式结果](testing/mysql-strict-tls-v495.md)

2026-11-07 V501。当前候选 389a9c68 在共同索引条件复验客户七步，8,624 次请求和每端 616 条墓碑核对通过。两档均无稳定退化，保留单并发第三轮触线。补齐该场景 12 个累计 CPU／边界内存窗口，测试资源已独立核清。整体首版性能继续按 V08 跟踪。见 [V501 正式复测](testing/paired-current-write-resources-v501.md)。

2026-11-07 V502。沿用 V501 同一当前镜像完成四读 19,336 请求，六轮均未触线，12 个累计资源窗口、29 份证据及资源回收独立核对。V471 历史归档另完成 1,313 条驱动命令直接表目标复核，间接访问及整体语义继续跟踪。见 [V502 正式复测](testing/paired-current-read-resources-v502.md)。

2026-11-07 V503。沿用 V501 同一当前镜像，在共同业务及文件索引下完成 4,834 次受保护附件下载，全部通过长度与 SHA-256 核验。单并发第三轮 p95 触线，两档均未触发稳定退化。12 个累计资源窗口及资源回收已独立核验。见 [V503 正式复测](testing/paired-current-protected-file-resources-v503.md)。

2026-11-07 V504。沿用同一镜像复测原客户、合同和合同明细三页导出，完成 2,784 次回放，1,392 次下载逐格核对，六轮均未触线。12 个累计资源窗口、735 份证据及资源回收已独立核验。见 [V504 正式复测](testing/paired-current-excel-export-resources-v504.md)。

2026-11-07 V505。沿用同一候选复测客户和合同的 Excel 追加导入，928 次请求均成功，每端两表各新增 1,856 行，六轮未触线。12 个累计资源窗口、55 份证据和独占资源回收已独立核验。见 [V505 正式复测](testing/paired-current-parent-excel-import-resources-v505.md)。

2026-11-07 V506。当前同一 C# 候选在默认排他父锁下复测原合同明细同父追加导入，1,232 次请求均成功，每端新增 4,928 条明细逐值匹配。四并发首轮 p95 触线，两档均无稳定退化。12 个累计资源窗口、54 份证据和独占资源回收已独立核验。见 [V506 正式复测](testing/paired-current-child-excel-import-resources-v506.md)。

2026-11-07 V507。当前候选逐次关系修复的 208 次回放均完成 132 个值的实库读回，明细三轮达标，合同第 2、3 轮延迟触线形成稳定退化，继续阻断首版性能签收。208 个资源窗口、33 份证据及清理均已独立核验。慢请求主要发生在 MVC 动作内，尚待分段定位。见 [V507 正式复验](testing/paired-current-maintenance-repair-resources-v507.md)。

2026-11-07 V508。同一候选完整关系维护矩阵 208 次回放均完成，六组达到稳定性门槛，保留明细单次修复第 2 轮触线。18 对、48 个资源窗口、28 份证据及清理均已独立核验。V507 逐次修复合同性能阻塞仍须单独解决。见 [V508 正式复验](testing/paired-current-maintenance-matrix-resources-v508.md)。

2026-11-07 V509／V510。同一候选逐次修复诊断定位到事务提交调用。复验核对 108 个 C# 请求，合同最慢 534.0 ms 中 CommitAsync 占 469.4 ms。参数摘要上限、提交服务器侧待定位和原 V507 正式性能阻塞分别保留。两轮合成资源已独立核清。见 [V509／V510 诊断](testing/paired-current-maintenance-commit-diagnostic-v509-v510.md)。

2026-11-07 V511／V512。保持相同持久化配置的独占 MySQL 诊断核对 104 次客户端／服务端提交。合同最长 390.4／390.1 ms，其中 binlog 同步 275.5 ms。明细最长 530.7／530.5 ms，剩余服务端等待继续定位。两轮独占资源已核清，V507 正式性能阻塞仍保留。见 [V511／V512 诊断](testing/paired-isolated-mysql-commit-diagnostic-v511-v512.md)。

2026-11-07 V513／V514。V513 独占 MySQL 双端诊断回放超时，留存 99 条成功 HTTP 回执，资源已核清。V514 回到原共享 MySQL、关闭观察器，208 次逐次修复和独立实库读回完成，合同及明细各三轮均未触线，28 份证据及资源回收已核验。V507 的历史退化保留，整体首版签收仍待完成。见 [V513—V515 记录](testing/paired-mysql-commit-v513-v515.md)。

2026-11-07 V515。同候选、同正式预算复验。208 次回放及独立实库读回完成，合同三轮均达标，明细首轮 p95 触线，未达到稳定退化条件。28 份证据与独占资源已核清。见 [V513—V515 记录](testing/paired-mysql-commit-v513-v515.md)。

2026-11-07 V516。当前候选的公开 R1 固定模型通过移动 Web 原附件上传、绑定、重开、受保护下载及注销，独立实库与存储字节一致，匿名访问返回 401。两轮测试资源已核清。其他文件类型、角色组合及 Web 整体回归继续。见 [公开 R1 移动附件 V516](testing/public-r1-protected-file-mobile-v516.md)。

2026-11-07 V517。非冗余详情、写后回显及已选确认省掉无用的分页 COUNT。四种跨表键型和两项生成 HTTP/MySQL 回归通过，原存储校验与当前读锁核对一致，22 张合成表已独立核清。见 [非冗余主键回读 V517](testing/nonredundant-key-read-v517.md)。

2026-11-07 V518／V519。固定 b6f8b251 的 Windows 九个后端项目、前端及 Agent CLI 通过，双轮再生成稳定。生成器全量发现原 Excel 离线夹具投影不匹配，修正后 13 项定向通过。Server-200 同库与分库 Production 空库、原权限及事务复验通过，资源独立核清。当前生成器全量、CI 和首版总验收继续。见 [当前回归与空库复验 V518／V519](testing/current-windows-bootstrap-v518-v519.md)。

2026-11-07 V521／V522。固定 `10cf15ea` 的独立包通过 Production 空库安装、原身份和附件 HTTP，实际删除故障后用同一文件 ID 重试成功。公开 Full 固定 1,284 份包文件，六个 R0 模型通过普通账号原 Excel 覆盖、实库并发锁、业务与附件整批回滚。两轮独占资源已独立核清。见 [当前独立包与公开 R0 复验](testing/current-standalone-and-public-r0-v521-v522.md)。

2026-11-07 V523。复核 V471 双端实库归档，补齐合同预览、摘要重放及客户复制、改名与软删除共 12 个请求，重算 47 个参数值证明并核对原事务结局。去重后固定 108 个请求全部对应独立业务 SQL 证据。间接访问、全体命令语义及当前首版验收继续。见 [固定请求 SQL 复核](testing/paired-request-sql-coverage-v523.md)。

## V520 Windows 生成器第六组

2026-11-07，按 CI 第六组条件执行固定 `10cf15ea` 的 Windows 生成器。TRX 共有 618 项，592 通过、26 条件跳过、0 失败，用时 25 分 37 秒。V518 发现的 Excel 导入测试夹具问题已在本组通过原用例，核对第二数据行和固定中文列名。其他五组及最终 CI 单独跟踪。

2026-11-07 V525。修复原 List 筛选使用未声明详情缓存键的问题。固定完整包的桌面及移动 Web 在真实普通账号下通过分组候选、已选回读、原筛选及注销。独立 MySQL 核对合同和审计值前后一致，两轮资源均已核清。见 [正式复验](testing/original-list-form-web-v525.md)。

2026-11-07 V526。修复平台同源部署下附件下载重复 API 前缀。原共库桌面通过上传、绑定、重开、下载和匿名拒绝，MySQL 及真实 S3 独立核对，所有轮次资源核清。浏览器落盘文件名和偶发加载遮罩继续跟踪。见 [共库附件验收](testing/platform-shared-s3-browser-v526.md)。

2026-11-07 V527。修复受保护二进制附件的 H5 落盘文件名。原共库桌面 Web 实测中文、空格、ZIP 扩展名与原文件内容完全一致，真实 MySQL／S3 读回和匿名拒绝通过，独占资源核清。移动 Web 和偶发加载遮罩继续跟踪。见 [附件文件名验收](testing/protected-h5-download-name-v527.md)。

2026-11-07 V528。固定 `16f53107` 的原共库平台通过 390×844 移动 Web 受保护附件完整链路。重开记录零下载，点击附件单次下载，原名和 202 字节一致，独立 MySQL／S3 读回、匿名拒绝与两轮资源回收通过。同页空 PUBLIC 控件的布局裁切仍需对照。见 [共库移动 Web 验收](testing/platform-shared-s3-mobile-v528.md)。

2026-11-07 V529。固定 `984884c7` 的分库原客户页通过 390×844 移动 Web 单次受保护附件下载，业务库独立读回、平台库零文件／引用、S3 对象及回收通过。空附件按钮裁切在实际 DOM 对照中由两套构建样式共同复现，临时修正有效，产品调整待确认。见 [分库及布局对照](testing/platform-split-s3-mobile-layout-v529.md)。

2026-11-07 V530。从 `da14b07c` 重新生成的独立完整应用通过 390×844 移动 Web 原附件链。绑定前两次拒绝均无弹窗，保存重开保留级联，单次按原名下载、MySQL／S3 独立读回、匿名 401 和资源回收通过。同期核对 Windows 生成器第 1 组 275 通过、47 条件跳过，第 2 组仍在执行。见 [独立移动 Web](testing/standalone-s3-mobile-v530.md)。

2026-11-07 V531。用当前源码 `0b8e8c0e` 重建相同模型和 Linux 性能镜像，原元数据、生成 C# 和手写规则逐字相同。沿用 V515 同条件合同与明细逐次修复负载，六轮均达到延迟和吞吐门槛，208 个资源窗口及 28 份证据独立核对，独占运行资源核清。当前镜像其他性能场景继续。见 [当前逐次修复复验](testing/paired-current-repair-performance-v531.md)。

同轮 Windows 生成回归第 2 组暴露 5 项旧断言，仍要求拒绝 V499 已验收开放的 R1 共享级联组合。只更新测试预期，独立检出中共享级联及相邻发布门禁 35 项通过、零失败零跳过，包含原组件、手写保护及异常模型拒绝。原分组继续保留，完整矩阵另验。

2026-11-07 V532。复用 V531 当前镜像完成原四读 19,336 次请求。正式 4,800 个四步场景响应一致，两档各三轮均未触线，12 个资源窗口及 29 份证据独立复核，运行资源核清。本机数据层 1,594 通过、32 条件跳过，生成 CI 六组发现 3,069 项、零遗漏重复。见 [当前四读复验](testing/paired-current-indexed-read-v532.md)。

2026-11-07 V533。当前镜像原客户七步复验共 8,624 次请求，单／四并发各三轮均达标。两端响应一致，每端 616 个墓碑、共同索引、12 个资源窗口、28 份证据及独占资源回收已核对。本机前端 364 项、API 225 项通过，API 49 项实库条件跳过。见 [客户七步复验](testing/paired-current-customer-write-v533.md)。

2026-11-07 V534。当前镜像的受保护附件下载完成 4,834 次。单并发第三轮 p95 触线，其余五轮达标，两档均未达到稳定退化条件。共同文件访问索引、完整内容、18 个业务回包、32 项物理读回、12 个资源窗口和 68 份证据已核对，独占资源核清。见 [受保护下载复验](testing/paired-current-protected-file-v534.md)。

2026-11-07 V535。当前镜像原客户、合同和合同明细三页 Excel 导出完成 2,796 次请求，六轮均达标。1,392 次下载逐格核对，独立解析 699 份不同工作簿。12 个资源窗口、735 份证据及独占资源回收已核对。见 [三页导出复验](testing/paired-current-excel-export-v535.md)。

2026-11-07 V536。当前镜像原客户和合同追加导入完成 928 次请求，六轮均达标。两端各新增 3,712 行，外键、缓存名、原种子和两份真实工作簿独立核对。12 个资源窗口、55 份证据和独占资源回收已核验。见 [客户和合同导入复验](testing/paired-current-parent-excel-import-v536.md)。

2026-11-07 V537。当前镜像原合同明细同父追加导入完成 1,232 次请求，两端各新增 4,928 行，父 ID 和缓存名独立读回。12 个资源窗口、54 份证据及独占资源回收已核验。单轮触线出现在4 并发第 3 轮（p95 比值 1.1604，吞吐比值 0.9215）。两档均符合稳定性门槛。见 [同父明细导入复验](testing/paired-current-child-excel-import-v537.md)。

2026-11-07 V538。当前镜像原关系维护完成合同及明细六组、208 次回放，18 对延迟和吞吐均达标。修复及零变化读回、48 个资源窗口、28 份证据和独占资源回收已核对。明细单次修复一轮 CPU 高于 Java。见 [关系维护复验](testing/paired-current-maintenance-matrix-v538.md)。

2026-11-07 V539。固定当前 C# 镜像重跑两端共 108 个原请求，独立核对 1,313 条驱动命令的默认库和直接表目标，双库物理对象及固定请求业务语义。132 份证据、审阅密钥备份和独占资源回收已核对。见 [当前 SQL 复验](testing/paired-current-sql-coverage-v539.md)。

2026-11-07 V540。当前原客户复制正式回放触发 15 分钟上限，11 个单端轮次和五对响应已核验，最终 C# 四并发与实库读回待复验。32 份证据及独占资源回收已核清。见 [复制超时证据](testing/paired-current-customer-copy-v540.md)。

2026-11-07 V541。当前平台 HBuilderX 导出 156 份 H5 文件，500 份受管源码逐字保持。独立复核平台共库、分库及独立应用三次移动 Web 附件证据，确认冻结附件组件与当前一致，并逐项核对产物与清理回执。见 [当前 Web 证据](testing/current-web-artifact-evidence-v541.md)。

2026-11-07 V542。当前原客户复制完成 8,624 次请求，六轮整场景达标，两端各 616 对源和复制件墓碑与原种子独立读回。复制接口每档各一轮 p95 超过 1.10，单独记录。12 个资源窗口、28 份证据和独占资源回收已核对。见 [复制完整复验](testing/paired-current-customer-copy-retry-v542.md)。

2026-11-07 V543／V544。平台分库原移动 Web 已通过两种普通角色的公开及受保护图片预览。遮罩残留、独立物理读回与匿名探针仍待修复或补验。两轮独占资源已核清。见 [图片专项](testing/platform-split-s3-picture-web-v543-v544.md)。

2026-11-07 V545／V546。平台分库两种普通角色图片、独立 MySQL 引用与匿名公开／受保护边界已复验。旧单对象探针拒绝双对象，修正已通过离线门禁。真实双对象 SDK 验证及移动 Web 遮罩修复继续。见 [实库复验](testing/platform-split-s3-picture-readback-v545.md)。

2026-11-07 V546。独立 S3 SDK 读回两张 PNG 的字节与摘要，按准确清单逐项回收并核清。移动 Web 写者保存重开仍需刷新，遮罩生命周期继续修复。见 [实库复验](testing/platform-split-s3-picture-readback-v545.md#v546-双对象独立读回与回收通过)。

2026-11-07 V547／V548。原移动 Web 写者保存后残留加载遮罩已修复。HBuilderX 新产物及两普通角色无需刷新的图片预览、分库读回、匿名边界和双对象清理均实测通过。前端 365、装包 19 通过。见 [请求遮罩复验](testing/web-request-loading-v547-v548.md)。

2026-11-08 V549／V550。独立生成应用和平台共库双图片验收完成。与 V548 合并，当前修复版三种部署均有两普通角色、移动 Web 无刷新预览、MySQL 引用、匿名边界和精确 S3 回收证据。见 [双图矩阵](testing/current-mobile-s3-picture-matrix-v549-v550.md)。

2026-11-08 V552。生成新增及导入父锁只读取主键，实际 MySQL 验证六条锁读、两次独立连接冲突、原归属拒绝及原成功响应。两轮独占资源核清。见 [父锁投影](testing/generated-parent-lock-projection-v551-v552.md)。

2026-11-08 V553。两种反查独立应用以当前生成器重建并通过原移动 Web 登录、复制、普通账号只读和同会话撤权。原表结构、复制落库及资源回收均已独立核对。见 [V553 反查应用移动验收](testing/imported-application-mobile-v553.md)。

2026-11-08 V554。当前 CLI 重生成固定性能样本后，294 份后端构建输入及八个程序集的方法定义和 IL 与原样本核对一致。原测量继续保留，此轮未发起新负载。见 [性能样本输入复核](testing/current-performance-inputs-v554.md)。

2026-11-08 V555。当前 Production 平台在 Server-200 分库验证同名源表，保存业务库实际列及索引，原两库数据保持。跨项目重放和限定库名被拒绝，两轮资源独立核清。见 [分库同名归属](testing/schema-import-split-target-v555.md)。

2026-11-08 V556。修复原单步公开上传在 S3 写入回执丢失后的孤儿对象。旧共库宿主实库复现，修复后共库与分库均通过真实 SeaweedFS 故障、条件删除和独立 MySQL／对象读回。三轮资源核清。见 [回执丢失回滚](testing/legacy-s3-ambiguous-rollback-v556.md)。

2026-11-08 V557／V558。当前公开 Full 独立应用在真实 SeaweedFS 验证旧单步回滚、分步保留对象、不同字节拒绝及相同字节并发恢复。同地址重启后原两普通会话和上传计划仍有效，原绑定、权限、Range 和实库回收通过。见 [独立应用重试与重启](testing/standalone-s3-upload-retry-v557-v558.md)。

2026-11-08 V559。真实大小写敏感 MySQL 上，同项目的大小写名称冲突改为审核阶段拒绝。原精确物理反查与共享库不同项目各自登记实测通过。CLI 和服务共用完整标识符校验。见 [反查名称边界](testing/schema-import-name-boundaries-v559.md)。

2026-11-08 V560／V561。反查保存使用 MySQL 返回的实际物理名。大小写不敏感的独占 MySQL 实测两种别名指纹一致，审核及配置使用同一名称。原大小写敏感模式的真实双表与跨项目归属回归通过。见 [物理表名实测](testing/schema-import-physical-name-v560-v561.md)。

2026-11-08 V563。平台及公开 Full 的 Web 宿主可按显式受信代理配置区分登录来源。两端真实 TCP 回环验证原限流、不同客户端、伪造及错误配置，受管升级后的旧包已实际复验。真实网关及完整登录仍按首版交付条件核验。见 [代理专项](testing/web-proxy-client-ip-v563.md)。

2026-11-08 V564。固定 55001214 的平台与公开 Full 在受控同源网关、真实 MySQL 和 Redis 下通过原密码登录、跨宿主重启锁定、独立 IP 审计、菜单及注销。实际部署网关和浏览器由首版总验收继续跟踪。见 [同源网关登录实测](testing/web-gateway-auth-audit-v564.md)。

2026-11-08 V565。当前产品的两宿主以 MySQL VerifyFull 和 TLS 1.3 通过显式安装、登录及原业务 CRUD。普通启动与证书拒绝均保持三个空库，独立核对最终 24／13／13 表和两端业务落库。见 [数据库 TLS 实测](testing/mysql-strict-tls-web-v565.md)。

2026-11-08 V566。当前两套 H5 已导出并通过受控同源 HTTPS 原接口。平台原桌面经本机 HTTP 验收桥通过登录、项目、菜单和注销。Windows 临时证书导入未完成，浏览器直接 HTTPS 及当前独立应用移动视口继续验收。见 [Web 网关分项证据](testing/web-https-gateway-v566.md)。

2026-11-08 V567。当前公开 Full 的原独立应用 390×844 浏览器通过两个普通角色登录、CRUD、只读写拒绝、删除取消及注销。真实 33 请求和 MySQL 持久状态核对通过。浏览器直连 HTTPS 继续跟踪。见 [移动 Web 实测](testing/current-standalone-mobile-crud-v567.md)。

2026-11-08 V568。当前候选 `960b9daa` 在 Server-200 重建固定 Full 三表样本，使用原 Java 对照重测客户与合同四个读取接口。8 首检、128 预热、19,200 正式请求及 4,800 组响应指纹一致，p95 比值 0.6207–1.0272，吞吐比值 1.0073–1.4705。0 轮触线，两档均未达到稳定退化条件。 12 资源窗口及 29 份证据独立复算，运行资源核清。 其他性能及最终交付条件继续核验。见 [四读正式复验](testing/paired-current-indexed-read-v568.md)。

2026-11-08 V569。当前 V568 镜像与原 Java 对照完成客户七步写入实测。224 预热、8,400 正式请求和 600 对响应摘要一致，每端 616 个墓碑。p95 比值 0.7920–1.0412，吞吐比值 0.9818–1.2109。0 轮触线，两档均未达到稳定退化条件。 12 资源窗口、28 份证据及资源回收独立核对。 事务与文件回滚本机专项 18 项通过。其他性能和首版交付条件继续核验。见 [七步写入复验](testing/paired-current-customer-write-v569.md)。

2026-11-08 V570。当前 V568 镜像与原 Java 对照完成本地存储的受保护文件下载实测。2 首检、32 预热、4,800 正式完整下载及 18 个原业务回包、32 项物理读回通过。p95 比值 0.4074–1.1571，吞吐比值 1.2060–2.4150。1 轮触线，两档均未达到稳定退化条件。 12 资源窗口、68 份证据及资源回收独立核对。 当前文件模块本机 261 项通过、60 项条件跳过。其他存储与性能项目及首版交付条件继续核验。见 [受保护文件复验](testing/paired-current-protected-file-v570.md)。

2026-11-08 V571。当前 V568 镜像与原 Java 对照完成客户、合同及合同明细三页 Excel 导出。2,796 次请求，1,392 次回放下载逐格核对，独立解析 699 份不同工作簿。p95 比值 0.4331–0.8484，吞吐比值 1.2004–2.1045。0 轮触线，两档均未达到稳定退化条件。 12 资源窗口、735 份证据及资源回收独立核对。 当前导出模块本机 36 项通过。导入与其他首版条件继续核验。见 [三页导出复验](testing/paired-current-excel-export-v571.md)。

2026-11-08 V572。当前 V568 镜像与原 Java 对照完成客户和合同的 Excel 追加导入。64 预热、864 正式请求完成，独立复读两工作簿，每端两表各新增 1,856 行并保留原样本。p95 比值 0.2107–0.3367，吞吐比值 3.5583–5.1194。0 轮触线，两档均未达到稳定退化条件。 12 资源窗口、55 份证据及资源回收独立核对。 当前生成器本机导入相关 8 项通过。同父明细和其他首版条件继续核验。见 [追加导入复验](testing/paired-current-parent-excel-import-v572.md)。

2026-11-08 V573。当前 V568 镜像与修补父 ID 的 Java 对照完成合同明细同父 Excel 追加导入。32 预热、1,200 正式请求完成，独立复读工作簿，每端新增 4,928 条明细并核对父关联。p95 比值 0.5322–1.1863，吞吐比值 0.8747–1.4241。出现稳定退化，本轮阻断首版性能验收。 12 资源窗口、59 份证据及资源回收独立核对。 当前生成器本机导入相关 6 项通过。其他导入模式、性能及首版条件继续核验。见 [追加导入复验](testing/paired-current-child-excel-import-v573.md)。

2026-11-08 V575／V576。当前同父候选分组采样完成 72 个原请求和每端 288 行独立读回。四并发组 C# 父锁及锁内处理较短，提交尾延迟较长。启动失败及旧审阅器不识别合并命令的证据留存，两轮资源已核清。后续持续采样用于定位，V573 正式性能阻断继续。见 [同父时序诊断](testing/paired-current-child-lock-timeline-v575-v576.md)。

2026-11-08 V577。同父持续四并发以独立只读连接取得 392 个聚合状态样本。C# 减 Java 的等锁为 -0.9／+22.7／+24.8，提交为 -12.9／-23.4／+7.3 毫秒／请求。1,200 正式请求及每端 4,928 条明细通过，资源核清。带探针结果仅作归因，V573 正式性能阻断继续。见 [持续状态诊断](testing/paired-current-child-continuous-sampling-v577.md)。

2026-11-08 V578。同父持续四并发按命令特征取得 395 个聚合样本。三轮均识别到 C# 合并追加命令，占用 14.5／14.6／17.1 毫秒／请求。 1,200 次请求及实库明细通过，资源核清。正式 V573 阻断继续。见 [合并追加状态诊断](testing/paired-current-child-packed-state-v578.md)。

2026-11-08 V579。同一 C# 候选按 V573 原条件完成无探针正式对照。32 预热、1,200 正式请求与每端 4,928 条明细通过，四并发吞吐 0.9826／0.9766／0.9459，2 轮触线。出现稳定退化，正式性能阻断继续。 12 资源窗口、59 份证据和资源回收独立核对。 见 [无探针对照](testing/paired-current-child-no-probe-v579.md)。
