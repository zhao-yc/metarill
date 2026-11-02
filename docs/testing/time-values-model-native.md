# TIME模型ID归一后的正式复验准备（C104）

日期：2026-11-02。状态pending／not-started，**仅准备及只读预检完成，正式未执行**。C102已失败清理并结束，不复用旧授权。

## 当前固定来源及前检

固定da137bba96fa82d8c0f5f60153658805f3cc6816独立普通克隆，2405份来源／152份正式输出／947份公开FullApplication包文件。严格Release零警告／错误，固定30项全部通过、0失败／0跳过（TIME准备17、实例输入8、HTTP5）。与主仓C103的30项重叠，不累加为60。723原前端／元数据／生成及手写保护文件逐字保持。

C103只在TIME隔离模型归一sample_customer_id／sample_contract_id为int64／bigint，整个三表50列／6索引／4TIME在建库前正反编译零差异，原共享样例不改；正式反查断言仍在，增加实际snapshot／Issues留证。[原失败与修正](time-model-roundtrip.md)完整保留。

本轮同一build_configuration实际生成假口令设置，送入本轮固定已编译C#外部传输分支，1项通过并写databaseOpened=false／applicationStarted=false回执；只换C100旧实例字段形状，同一入口1项预期失败且无回执。两配置、回执／TRX／执行器摘要绑定，执行前重算；不把正反例当作真实业务结果。

外层8个未授权零副作用检查、正式入口5个实际拒绝（pending／ended／旧用途／末尾换行／旧pin）及check-only通过。新SSH只读核实固定实例、本轮库／账号／所有权不存在、默认SESSION舍入前提满足；未建库或执行TIME业务。

## 待直接授权范围

运行 `7d03364dd2e5f552`，Server-200唯一库 `metarill_fresh_7d03364dd2e5f552_agent`、临时账号 `mtf_7d03364dd2e5f552@%`，范围SHA256 `d41d9d40485813e2216a52739286f3c8b4e6874774f1d7bab43997936f875a8c`。精确计划见本机忽略目录 `.local/time-values-model-native-20261102/permission-plan.md`。

只运行一次正式三表TIME用例，900秒；三业务表各最多512条合成记录，加11基础表共14表，不额外CHECK。验证Production空库不自动建表、显式安装及完整反查、初始化所有者原Web三字段登录、四种TIME值／省略null／CRUD／筛选复制／原XLSX及导入计数回滚／重启软删。仅本轮独占连接改两种SESSION sql_mode、更新本轮样本后回滚并恢复会话值，不改GLOBAL。

登录是初始化所有者，不记普通权限矩阵；模式对照不记两模式完整HTTP；导入失败只核行数，不宣称已观察首DML／文件回滚。该驱动不重复安装、不验证同地址或注销、不执行SheetJS、真实浏览器、真机、并发或性能。

成功或失败均按固定实例／名称／所有权清理同轮唯一库／账号，新连接和独立新SSH核零；只在自有宿主停止且文件摘要复核后删除 `E:\git\metarill\.local\time-values-model-native-20261102\frozen-source\.local\time-values-native\7d03364dd2e5f552\evidence\files` 下本轮GUID合成uploads／exports及临时私有凭据。保留固定来源／包／工程／工作簿核验副本及全部证据。DROP及文件删除不可逆，仅本轮新样本；失败不自动复用或重试，清理异常按项目规则保留／备份恢复材料。

C104尚未取得直接授权或正式结果，C38与MySQL首版总验收未完成；未合main或部署生产。B46旧12表已按独立授权清理完成，不属于本次新增范围。
