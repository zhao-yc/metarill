# R0跨表覆盖导入的逻辑匹配与解析上下文

2026-11-02，B124；起点e97b2fb。本项补生成器内部覆盖读取，不改原页面、表头、请求、元数据源或完整包发布策略。内部生成与离线验证已通过，真实MySQL覆盖写入／并发仍未执行。

## 缺口

原OverWriteByFirstColumn先按表级字段顺序选首列，再直接在物理实体Scope上等值匹配。R0跨表的合法稀疏记录只存所选最深级ID，物理祖先列可以为空。若祖先ID作为首列，物理比较不能等同于该记录的逻辑祖先。同样，候选父依赖原记录时，不能从稀疏实体的空祖先槽建立解析上下文。

## 接线

新增内部覆盖匹配计划，元数据与跨表目标页绑定在生成期固定；在原导入事务中按原项目、父范围及页面条件进行最多两条当前读，不执行COUNT。超过一条仍按原重名规则拒绝，唯一匹配后按主键取得锁定物理实体，继续调用原更新入口；物理实体消失则拒绝，不降级新增。

解析依赖使用私有只读记录，读取必要逻辑列，不构造或修改持久实体，不触发手写Entity构造函数，也不向更新DTO补交省略字段。明确null覆盖手写构造默认，不把空父回退到默认候选。匹配计划的私有列不进入普通页面VO。R1、追加和没有跨表的原路径保持；普通物理键且无逻辑上下文时不创建新匹配计划，继续原查询；仅逻辑级ID或需还原的上下文使用新路径。匹配键和上下文均声明为真实查询依赖，不能借延迟展示的空占位充当逻辑值。

所有数据库表达式继续由数据适配层编译，生成Service不判断数据库品牌或解释任意SQL。跨表覆盖导入的公开Full门槛仍保留；不能因BackendOnly生成或计划装载成功解除完整身份／文件／事务验收要求。

## 当前验证与失败保留

- 生成库严格Release、warnaserror编译零警告／错误。
- 两种ID类型实际生成并构建，真实装载的私有计划仅返回主键／匹配列，祖先表达式不是物理空槽；元数据不变，手写扩展两轮保护。
- 首轮定向5通过／1失败：六模型原首列含附件数组，原覆盖数组门禁正确拒绝。仅在测试目录明确配置名称为首列，未放宽数组规则；第二轮6通过、0失败、0跳过。
- 扩展上下文测试初次6通过／1失败：夹具给稀疏祖先声明DB默认，违反现有存储协议。保留拒绝，改用合法手写partial构造默认99；实际生成解析器定向1通过，逻辑父正常恢复、null保持、只提交名称不补父字段。
- 首次扩展58通过、0失败、0跳过，覆盖原依赖、原Excel列、无损路径、公开Full与否及新逻辑匹配；这是最终触发优化／私有上下文类型调整前的组合，不能转记为后续全量CI。
- 最终版本21通过、0失败、0跳过：两种ID及六个原级联控件作为覆盖首列的模型、名称物理首列原路径、逻辑父上下文与省略／null、R1／追加／普通来源保持、原数组首列拒绝及原依赖规则。包含1个BackendOnly包从自身Core／Data源码restore及build，未借工作区DLL；不冒充公开Full。
- 名称首列最初用于修正六模型夹具的数组错误；最终六模型明确改为原级联控件首列，以实际装载逻辑分支，名称首列另用独立生成项目验证原路径。中间20项、单包1项及上下文单项均与最终21项重叠，不相加。
- 最终CLI严格Release构建零警告／错误；平台和示例正规再生成两轮，215生成文件及全部723个原前端／元数据／生成保护文件逐字不变。无手改生成产物。
- A30／B123各自check-only保持frozenVerified=true、pending／not-started、子进程0、databaseWrites=false；本项不消费其授权。

材料保存在忽略目录.local/cross-overwrite-logical-match-20261102，原始日志、TRX与保护基线保留。本项尚未执行实库DDL／DML、真实OAuth／浏览器／真机或性能；A30、B123原独立固定材料不因本项改变用途或授权。

## 后续与跟进

生成、包编译和原文件保护已登记进度表。正式覆盖导入仍须独立证明同项目／父范围匹配、歧义、路径／候选上下文、整批文件与DML回滚、旧快照及并发；不将本项转记为B14或MySQL首版完成。

采用配套生成器后正规再生成受影响C#Service及其清单，保留手写partial；完整应用需按既定门槛重新打包／验收。不需要DDL、存量数据回填或原前端变更；回退使用匹配的生成器与生成产物，不改业务数据。


核心复核命令：

```powershell
$env:DOTNET_HOST_PATH='E:\git\metarill\.tools\dotnet\dotnet.exe'
.tools/dotnet/dotnet.exe test backend/tests/Metarill.Generation.Tests -c Release --nologo --no-restore --disable-build-servers --verbosity quiet -warnaserror --filter 'FullyQualifiedName~NonRedundantImportMatchTests|FullyQualifiedName~ImportDependencyTests|FullyQualifiedName~CrossTableExcelImportColumnsTests'
.tools/dotnet/dotnet.exe build backend/src/Metarill.Cli -c Release --nologo --no-restore --disable-build-servers --verbosity quiet -warnaserror
.tools/dotnet/dotnet.exe backend/src/Metarill.Cli/bin/Release/net10.0/Metarill.Cli.dll generate --metadata backend/metadata/platform.json --output backend/generated/platform --namespace Metarill.Platform
.tools/dotnet/dotnet.exe backend/src/Metarill.Cli/bin/Release/net10.0/Metarill.Cli.dll generate --metadata backend/metadata/demo.json --output backend/generated/demo --namespace Metarill.Demo
```
