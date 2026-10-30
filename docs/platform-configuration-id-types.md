# 平台配置关联ID类型统一

状态：A15固定六字段子集已完成，源头、本地回归及获授权后的三表真实MySQL验收通过并清理。仅处理下表六个真实关联，不代表所有表结构优化完成。

| 表 | 字段 | 原逻辑类型 | 新逻辑/物理类型 | 目标主键 |
| --- | --- | --- | --- | --- |
| config_child | child_config_page_id | string | int64 / bigint | config_page.id |
| config_column | link_config_table_id | string | int64 / bigint | config_table.id |
| config_column | link_config_page_id | string | int64 / bigint | config_page.id |
| config_page_column | link_config_table_id | string | int64 / bigint | config_table.id |
| config_page_column | link_config_page_id | string | int64 / bigint | config_page.id |
| config_page_column | combine_super_column_id | string | int64 / bigint | config_column.id |

## 元数据与生成源头

统一规则位于 `scripts/build-platform-metadata.mjs`，同时核对稳定表/字段ID、原字段名和目标声明。只匹配这六条，不按_id后缀批量改类型；sys_agent_grant.client_id和sys_agent_task.request_id继续是字符串。当前模型排序字段已为int32，本轮未删字段、索引或修改空值/必填、页面、菜单与权限声明。

原类型下拉只有“整数/int”，保留该选项值；逻辑类型使用int64，由MySQL适配器编译为bigint。整数长度清空，旧类型展示标签按原整数选项更新。实体、DTO/VO、Service及归属清单由正规CLI生成，没有手改.g.cs。固定管理模块内容未变，仅其平台来源摘要和配套校验和随源头更新。

## 原界面与协议保持

直接让前端使用新存储类型，会把两份原详情的maxlength/校验规则改为数值。为遵守原页面/样式/交互不变的要求，`PlatformConfigurationIdPresentation`只在平台固定六字段生成前端时建立原展示副本；原前端源、控件、初值、校验和事件逐字保持。后端类型、能力审计、写入定义和数据库结构始终使用真实int64模型，不通过展示副本放宽任何能力。其它项目及可信安全模块不套用此转换。

实际实体以long?持久化，HTTP公共JSON仍输出字符串ID；原选择器数字/字符串输入均无损。DTO继续区分省略、null和空选择字符串，溢出拒绝。元数据加载仍把数据库long转换为Core原字符串标识，包含超过2^53及long.MaxValue的精度验证。可空存储不解除原页面必填/关系规则；存储测试不代表必填子页可以清空保存。

转换只发生在生成期，不在CRUD请求中额外解释元数据；本轮不宣称延迟、吞吐或跨数据库性能改善。

## 接入与兼容边界

新平台空库使用本版派生元数据和正规生成物，明确预览并安装结构后再执行原自举流程。旧开发库若仍为varchar/nvarchar，不能仅替换程序就认为完成升级：先审查实际值、空字符串、非法/超限ID和结构差异，使用独立备份或新隔离库验证；默认结构适配器不自动承诺字符串到bigint的无损转换。种子只补缺，不覆盖既有元数据，普通启动不DDL或回填。本首版仍不承担旧YZP数据/业务代码迁移；本轮没有改Server-200现有平台配置表或执行此类型转换。

平台后续更新需同批同步builder输出、生成器、生成物及固定模块来源摘要，保留手写partial。普通业务项目不因平台固定ID优化而被改型或套用展示回退。回退使用上一完整配套版本；若以后已经显式变更数据库，结构/数据回退需另按实际备份和评审计划执行，不能用重启或覆盖种子代替。

## 已执行验证

- 全部元数据派生43/43通过；固定六字段与目标主键同型，稳定ID和页面/菜单未改，拒绝错配身份或目标。
- 实际平台实体/DTO14/14通过，覆盖无损字符串ID、省略/null/空选择与溢出；元数据全项目330通过/25实库跳过，六字段解码另有定向复核。
- 两份原详情通过正式生成落盘的精确SHA校验，2/2通过；其它项目不套用平台展示规则。初次比较未规范行尾的渲染中间文本导致1项失败，改为正式落盘比较；随后测试变量声明编译错误已修正，原失败日志保留，没有替换预期页面摘要。
- 两轮平台/示例正规生成215文件逐字稳定，含189受管输出、2清单、24手写partial；全部生成前端及489份受版本管理的原工程文件与修改前逐字一致。
- API全项目68通过/47实库跳过，零失败；其中本轮三表用例明确跳过，不计作实库通过。测试源码已严格编译零警告/零错误，租约门禁单测通过；最后回归以私有回执为准。
- 实际MySQL方言编译器离线核对真实平台六字段/目标主键均为bigint，并生成独占三表4/5/6列的3条CREATE SQL；没有连接或执行数据库。

私有证据在 `.local/platform-id-types`，包括修改前快照、精确元数据差异、前端/partial保护、TRX、原失败记录、离线DDL及SQL计划。实库曾被自动权限审核拒绝，因为这组新远端三表及其清理未获明确授权；没有绕过或执行。测试现在要求新的精确租约且连接前拒绝pending/已用/改名/元数据漂移，在独占锁内重新读取防止旧状态复用。该历史拒绝保留；后续用户通过call_876ffa4f029f4a0a8d7737dcd681a105明确批准本轮三表创建、验收与清理。仅在批准后执行，当前租约ended，不得复用。

三表真实驱动验证已完成；完整平台新空库自举、原浏览器/设备及同条件性能未在本轮执行。A15只标固定六字段子集完成；类型/索引优化汇总和MySQL首版不标全完成。没有合main或部署生产。

本轮代码/元数据/生成物摘要 `85dc8826bbceb8d1bc172733ec46162c0a58264e1533e12c8760a0548854aefe`，具体逐文件及各独立批次记录见私有 `.local/platform-id-types/validation-receipt.json`；批次存在重叠，不累加为首版全量。

## 获授权后的真实MySQL验收

固定提交 `1eb0c6da372093c3cd328b64cd36d8f71363b826` 独立普通克隆，共1855受版本管理文件、472个编译输出文件逐项固定摘要；严格Release编译零警告/零错误，私有配置移除时门禁1通过、真实用例1明确跳过。正式执行前后均核对源码及程序集摘要，没有使用随后变化的工作区代替。

本轮只在Server-200既有metarill_business_test操作 `mt_idt_b94d52de58c5_{config_child,config_column,config_page_column}` 三表，各1条样本；不建库、账号或GRANT，不修改旧平台表。实际用例1/1通过、零失败/零跳过，耗时3.6124527秒。真实SchemaService预览/明确执行三条CREATE后无结构差异；实际SqlRowStore写入六处bigint、当前生成实体FreeSql投影和公共JSON字符串ID精度通过，超过2^53的ID/目标值保持，空选择更新为null后读回通过。

测试仅清理已确认创建且结构/样本所有权一致的三表，再用新连接核验不存在；随后独立PowerShell进程再次校验实例并仅读information_schema，三表均零。租约ended、执行进程已退出，原授权审计及此前未批准拒绝记录保留，不重跑已用租约。证据为私有 `frozen-manifest.json`、`authorization.json`、`native-results/native.trx`、`execution.json`、`cleanup-independent.json` 和固定克隆内本轮intent/created/verified/cleanup回执。

此专项不等于完整平台新库自举、必填子页允许清空、浏览器/真机或同条件性能验收；旧库字符串到bigint仍须另行审查迁移，不因测试通过自动DDL或覆盖种子。

## C58现行模板兼容补验（2026-10-30）

用户后续批准List服务端搜索/滚动分页，page_column详情的完整脚本因此变化；A15历史两页SHA通过事实不改写。现测试使用bd800bfa的独立六字段原类型事实，分别正式生成当前存储模型与旧展示事实模型，两份详情整文件落盘字节相同；未受该变化影响的child页仍校验A15原整文件SHA，原DOM/样式和主前端十二页金样保留。七个相关完整类118项及前端320项零失败/零跳过，批次不累加；未新增实库或设备验收。详见[本轮失败、修正及证据](testing/ci-current-contracts.md)。
