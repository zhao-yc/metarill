# TIME完整模型反查往返前置（C103）

日期：2026-11-02，起点4c305e5。仅修测试夹具及诊断，不改产品、反查规则、原模板或页面。

## C102真实结果及清理

b99bd31／0fb47989582aad0e按本次直接授权call_294b3a88ebaf4b6aa1c725ab03c1e8fe:0单次执行，正式0通过／1失败／0跳过。原实例输入门禁已通过，真实Production空库启动不建表；显式14条建表语句成功，预览零差异，随后完整结构反查草案有不可表达项，断言失败。尚未执行后续初始化登录、TIME CRUD／SESSION模式／Excel／重启，不能把建表成功记为整条通过。

同轮宿主停止、files合成存储核清；唯一库／账号经原所有权清理、新连接及独立新SSH确认不存在，私有settings／ownership删除；execution为passed=false、cleanupConfirmed=true、ended、autoRetry=false。原固定来源／包／程序集及失败TRX再次核对保持，正式资源不复用。

## 两个旧字符串外键与修正

保留的完整元数据及纯正反编译离线复现定位两列：sample_contract.sample_customer_id、sample_contract_line.sample_contract_id。旧共享样例为字符串物理外键，当前配置写入／反向草案的既定ID规则要求int64，故报COLUMN_UNREPRESENTABLE／标识字段类型错误。原TIME准备只检查包编译和时间列局部规则，未事先检查整个模型反查。

仅在TimeValueNativePreparation.Catalog的隔离模型中把这两个关系字段设为int64／bigint，符合首期统一关联ID约定；共享v8-global-cached-sample.json和其它字符串关系模型不改。原三表50列／6索引、四TIME字段与默认、页面及交互保持。反查编译器和元数据ID规则未放宽，也不跳过整个模型断言。

新增Entire_time_catalog_roundtrips_before_database_resources，用整个实际TIME模型先正编译再反向草案并零差异往返，验证4TIME、两个int64物理外键及原共享样例仍为字符串。先在原夹具严格运行1项失败、TRX精确记录上述两列；修正后严格Release30项通过、0失败／0跳过（准备17、实例输入8、HTTP辅助5），包含真正Full从自身来源严格构建、两轮生成手写保护和原MVC／手动DTO／非管理／开发认证HTTP。与C101／C102的29项有重叠，不累加。

正式用例在反查断言前补schema-draft回执，保留实际snapshot／Issues，断言消息附表／列／问题码；不改成功标准。原723保护文件及共享样例字节保持，证据在.local/time-draft-diagnosis-20261102。

## 仍待完成

30项为本地构建／纯编译和隔离HTTP，不是新的TIME业务实库结果。旧C102单次授权已执行并结束，不能复用；需新固定来源、完整包、同一协调器实际配置传输及护栏通过后，再申请新精确范围复验。C38、普通权限矩阵、原浏览器／跨端／并发／性能和MySQL首版总验收继续，未合main或部署生产。

C104后续已从本修正da137bb独立固定2405来源／152输出／947Full，固定30准备及同一协调器实际配置传输正反例、8外层／5入口与只读预检全部通过；新精确范围仍pending，见[固定准备](time-values-model-native.md)。未把本地修复计作新的TIME实库通过。

C104随后按本次新精确直接授权完成正式1过／0败／0跳过：实际三表反查Issues=[]、两个外键bigint、四TIME持久化／原接口／XLSX／回滚／重启通过；本轮唯一库／账号／存储及私有凭据独立核清、ended。原C102失败不改写；详见[正式收尾](time-values-model-native.md)。
