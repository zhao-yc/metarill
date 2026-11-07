# 公开R0跨表Excel同包正式验收记录

日期：2026-11-02。编号B123；产品起点d90932ed5808f761591b8c84ca56a72e70f9155b。[B122](cross-table-excel-publication.md)已接通独立R0默认／OnlyNew追加和路径导出的公开Full，本项补公开同包的真实身份／MySQL验收驱动。**固定af419e7的公开Full六模型正式验收已通过，测试库、账号及合成文件已独立核清；仅关闭本固定子集，不改写B121内部包历史或宣布首版完成。**

## 相同模型与真正公开包

复用B121原六模型及其32业务表／32页／32菜单／12维护按钮，元数据、普通导航授权和业务断言不变。公开准备直接调用ApplicationPackager创建FullApplication／web-database-v1，不使用内部准备豁免、不改Program，不添加测试Actor。六份手写partial仍为原BeforeSave观察：只有实际首行DML、同一事务及一条文件引用均可见后才拒绝第二行；两轮正规生成保护它们。

固定验证要求公开标记、正确身份模式与namespace、完整元数据／安全摘要、受管源文件及六个精确手写钩子；编译输出与原固定全文件快照绑定，不能替换成其它包。多余源码即使重新捕获仍拒绝。执行只能使用新副本，唯一允许新增的受控文件是原宿主schema-history.jsonl；固定原包不运行。

## 独立入口与范围守卫

新入口：GlobalCachedCrossExcelNativeMySqlTests.Production_public_Full_six_models_roundtrip_original_excel_and_rollback_files。专属环境键METARILL_PUBLIC_R0_CROSS_EXCEL_NATIVE_SETTINGS，用途public-r0-cross-excel-native-v1；B121内部包用途与A30数值用途不能启用。

公开／内部两个固定入口调用同一私有真实业务流程，各自先校验自己的环境键、用途、状态、产品及固定根，随后才验证包、复制工程或读取连接。客户端JSON不能选择档案。公开用途还固定六模型、32业务表、43张总表及每业务表512条预算，运行ID严格16位小写十六进制并拒绝尾换行。

本轮正式执行的流程保持原Production启动不建表、显式结构安装／重复零差异、管理员重复初始化密码保持、原三字段普通账号登录／32菜单、六模型CRUD／原XLSX／稀疏存储／传播／维护／歧义及共享冲突、六CHECK首因和六次首行DML＋文件引用后的整批回滚、复制下载／撤权／同地址重启注销。公开回执明确publicFullApplication=true、preparedNotPublishedFull=false；不能把内部包回执改名为本项结果。

## 正式执行前的历史离线验证

最终严格Release组合 **37通过、0失败、2条件跳过**：公开准备、17项新租约负向、原内部准备／租约及导航合同。两个跳过分别是内部和公开正式新库用例；它们都没有取得本轮设置，不能计实库通过。公开和内部包分别独立构建，公开Production/Web的18个原匿名入口均401，未访问数据库。

实际已编译入口另有五个拒绝检查：公开pending、ended、借旧内部／A30用途，以及内部入口误传公开用途，均按各自精确授权错误拒绝。没有连接配置或执行目录，不与37项累加为正式通过。

首轮34通过／1失败／1跳过，新增校验只规范了实际手写文件行尾、未规范原钩子的CRLF；改为对称文本核对，同时先逐份证明六钩子原始字节与原函数一致，原快照摘要仍严格固定。第二轮仍34／1／1：新增测试错误要求重新冻结的编译输出自动等于旧冻结证明；纠正为所有输出必须由原证明拒绝漂移、托管源码重新捕获也拒绝，正式执行器仍必须固定最初批准的全部输出摘要，不允许换证明。原失败及TRX保留，没有修改产品、原钩子、页面或SQL语义。

## 正式执行前的历史准备

独立提交来源、正式程序集和公开完整包已固定，见下节；精确单次外层协调器、资源清单和只读预检现已完成，见末节；仍须本轮直接授权。当前没有新建资源、DDL或业务DML，没有正式Web／MySQL结果。A30仍维持其原固定批次和pending租约，不借用已结束B121授权，不消费A30。

后续即使本固定六模型正式通过，也不替代覆盖导入、R1、来源父范围／混合自树／重复角色、其它关系组合、真实浏览器／跨端／并发／同条件性能及MySQL首版总验收。未合main或部署生产。

## 独立固定批次完成（af419e7）

从af419e79ce9878a1ee88466017440feda9ee15ce建立独立普通克隆，2392份跟踪源码／152份测试输出／1266份真正公开Full包文件已逐项固定，原Git来源与全部文件摘要保持。使用当前固定SDK进行独立严格Release构建，零警告／错误；新固定程序集仅运行公开准备和租约，19通过／0失败／0跳过。该19项与前述37项重叠，不能累加；本批没有正式新库用例。

五个实际入口拒绝检查又在固定程序集及独立根下复核，均按预期拒绝、无数据库配置或执行目录；与主仓五项不累加。公开标记／Web身份／namespace、模型与安全绑定、Host输出及全文件集再次纯文件复核，frozenVerified=true。来源与固定包不启动，运行只允许使用未来的独占副本。

材料在本机忽略目录.local/public-r0-cross-excel-native-20261102：source-snapshot.json、formal-assembly-files.json、prepared-receipt.json、fixed-build-state.json、固定TRX和拒绝回执。固定阶段尚未分配新远端运行ID或租约；随后仅完成下述pending本地计划，未创建连接、资源或凭据。A30的原固定材料另行check-only通过，保持pending／not-started；没有消费任何新库授权，也不能将公开Full构建或19项离线通过记为正式业务通过。

## 精确单次计划与只读预检完成

运行c608cc7a102a817b，唯一计划库metarill_fresh_c608cc7a102a817b_agent、账号mtf_c608cc7a102a817b@%；范围摘要b8991e89db8b3ff51ce772a13abb767146757a5ca92be824a4e84b7bae693e41。32业务表／11基础表、六个精确命名CHECK、512行逐表预算及900秒均固定。外层绑定原af419e7源码、152测试输出、1266公开包文件及SDK／Server-200租约工具／协调器摘要；check-only纯文件验证通过，未因计划生成重建或替换包。

八项未授权／已用／审计缺失及不匹配／未知参数全部拒绝，子进程、秘密及所有权写入为零。与固定五实际入口拒绝分别记录，不相加为业务测试通过。只读新SSH核对Server-200和MySQL实例pin，新库／账号／所有权均不存在；无DDL或业务DML。

协调器只运行一次，完成后独立审查原菜单32项、六模型回执、六个实际CHECK首因及六次事务钩子和精确后置错误；失败不自动重试。停本轮宿主后只清storage/GUID合成文件，远端按实例及所有权清理同轮库／账号，用新连接和独立新SSH核零。清理异常保留恢复材料，不扩大范围。精确表、CHECK、权限、存储路径及不可逆清理说明在本机忽略目录.local/public-r0-cross-excel-native-20261102/permission-plan.md。

该段为执行前pending准备记录；本轮随后取得直接授权、单次执行并清理结束，正式结果见末节。A30另行失败清理，不影响或替代本结果。

## 2026-11-02 公开同包正式通过与独立清理

用户直接授权后，c608cc7a102a817b仅执行一次原af419e79ce9878a1ee88466017440feda9ee15ce固定来源：2392源码／152输出／1266真正公开Full文件。正式TRX为1通过、0失败、0跳过；六模型各自业务回执均通过。使用真实Production/Web与原三字段普通账号、原32菜单，不注入固定授权；publicFullApplication=true、preparedNotPublishedFull=false、fixedAuthorizationInjected=false，实际43张表。

六模型均通过原XLSX、稀疏存储、改名传播、原维护、真实部分DML及文件整批回滚、撤权；原流程同地址重启会话与注销拒绝通过。外层单独复核32个非super菜单的项目100／后台9归属及32个不同目标页；六次事务钩子均在实际首行DML和一条文件引用可见时触发。六个late_cache CHECK各出现一次精确首因、六条精确后置错误核对通过，没有将一般失败或前置拒绝当作回滚证据。

Server-200新连接及独立新SSH确认本轮唯一库metarill_fresh_c608cc7a102a817b_agent、mtf_c608cc7a102a817b@%不存在；原宿主退出及storage合成对象清理通过，私有settings／ownership已删除。execution回执passed=true、cleanupConfirmed=true、leaseStatus=ended、privateSettingsDeleted=true、autoRetry=false。原冻结材料、verified、六模型回执、cause-review、TRX及独立核零保留于.local/public-r0-cross-excel-native-20261102；一次性租约结束，不自动重跑。

本结果是正式HTTP／真实SQL及原SheetJS验证；browserExecuted=false、performanceExecuted=false，未执行浏览器点击、设备或同条件性能。B14覆盖导入、R1、来源父范围／混合自树／重复角色及其它组合继续，B124仍仅内部生成通过；未合main或部署生产，MySQL首版总验收未完成。
