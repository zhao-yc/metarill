# double数值XLSX导出与原导入往返

日期：2026-11-02。编号：A28。起点：`4c0ad12b922d8fac2c352beedffb977d6126ff35`。本轮保留原页面、SheetJS选择方法、导出列、数值单元格、下载票据和权限边界，修复服务端生成XLSX时的浮点文本失真。

## 实际失败与原因

先走真实ExcelPageExportService及下载流，再直接解压工作表数值节点，首轮0通过／1失败：原位型`0002228BA3BC2926`被写为`2.96900398299886E-309`，重新解析已不是原值。原始TRX保留，不修改期望或把浮点列整体改为文本。

当前固定ClosedXML 0.105.1的工作表写入最终调用ToInvariantString，而该版本对double明确使用G15。这与A27的默认R格式问题是不同的文本出口。[工作表写入源码](https://github.com/ClosedXML/ClosedXML/blob/0.105.1/ClosedXML/Excel/IO/WorksheetPartWriter.cs)、[数值写入扩展](https://github.com/ClosedXML/ClosedXML/blob/0.105.1/ClosedXML/Extensions/XmlWriterExtensions.cs)、[G15格式化源码](https://github.com/ClosedXML/ClosedXML/blob/0.105.1/ClosedXML/Extensions/ObjectExtensions.cs)。本轮没有升级或反射修改依赖。

## 修复与资源边界

- 原工作簿保存后，仅收集G15回读会改变位型的double／float单元格，以统一ExactNumericValue.FormatDouble生成可靠文本；正常0.1等原短文本不变。
- 存在失真时流式复制自有ZIP，只替换单工作表内指定数值节点，保留数值类型、样式及原表头／顺序。其余ZIP条目的解压内容逐字保持；不执行公式、不接受外部工作簿或路径，不向磁盘解压ZIP条目。
- XML禁止DTD及外部解析，目标必须是非公式数值且含完整数值节点；遗漏、重复目标工作表或异常节点明确失败。
- 不存在失真则沿原移动文件路径发布。修正路径的原文件和输出各自受MaxFileBytes约束，临时磁盘峰值最多为该项限额的两倍／任务；并发、行列、单元格、文本和票据预留仍走原门禁。增加一次扫描及必要时ZIP读写，尚未量化同条件性能成本。
- 原`.part`和最终随机路径在签发票据前保持活动跟踪。取消、读取故障或修正后文件超限都不发布票据，沿原finally清理两者并释放预留；并未新增持久目录、第三种临时文件或新后台任务。

## 验证结果

| 层次 | 本轮实际结果 |
| --- | --- |
| 导出整组严格Release | 35通过、0失败、0跳过；新增6项包含中法区域设置下各8299个不同有限位型，逐项核对实际XLSX数值文本及ClosedXML重新读取结果 |
| 原类型及包内容 | double／float、ID、金额、公式样式字符串、空值、日期、标题样式／冻结／列宽正确；全部非工作表ZIP条目内容逐字保持 |
| 失败与限额 | 原文件已经落盘后取消／IO故障，及原包可写但修正包超限，均拒绝并核对无遗留文件；随后合法导出成功，预留额度已释放 |
| 生成及完整包组合 | 18通过、0失败、0跳过；含实际生成Service／导入器／原Vue、两个独立Full包严格构建及新运行时文件与归属同时缺失的拒绝保护 |
| 原XLSX导入方法 | 真实票据XLSX经原SheetJS选择方法、原JSON回调及实际生成ParseImport101Async，8个正负相邻／极值／正常短值逐位保持；未替换SheetJS或改前端参数 |
| API整组严格Release | 223通过、0失败、49项实库条件跳过；跳过不记为本轮数据库验收 |
| 正规再生成 | 官方CLI严格Release；平台／示例各两轮215文件稳定，24份手写partial／499份原前端／9份元数据保持，共723保护文件无变化 |

各组与定向用例存在重叠，不合计为独立覆盖数。原方法测试只模拟本机文件选择与二进制传递，不创建浏览器或真实Web身份，不发送业务HTTP或执行数据库写入。全部日志、首轮失败及TRX保留于本机忽略目录`.local/double-xlsx-precision-20261102`。

主要命令：

```powershell
.tools/dotnet/dotnet.exe test backend/tests/Metarill.Exports.Tests -c Release --no-restore --disable-build-servers -p:TreatWarningsAsErrors=true
.tools/dotnet/dotnet.exe test backend/tests/Metarill.Generation.Tests -c Release --no-restore --disable-build-servers -p:TreatWarningsAsErrors=true --filter "FullyQualifiedName~DoubleStatisticsGenerationTests|FullyQualifiedName~GeneratedDoubleValueTests|FullyQualifiedName~Floating_statistics_candidate_requires_the_complete_bounded_runtime|FullyQualifiedName~Candidate_requires_complete_numeric_runtime_without_touching_target"
```

## 升级与未完成范围

既有C#应用需成套同步Core／Exports运行时，新FloatingWorkbookWriter.cs是当前完整候选的必要归属，双删源码和归属不能绕过校验；旧目标可通过原CLI／在线助手的显式运行时同步升级，手写partial保护保持。本轮不改变元数据、页面模板、接口或DDL，不需要回填旧业务数据；旧导出文件不被追改。回退应整套匹配原运行时，旧G15风险会恢复。

已证明的是原始XLSX包、ClosedXML读取及原SheetJS回调的往返。Microsoft Excel自身声明15位数字精度，且不支持非规格化数，不能把本轮结果外推为Excel桌面端打开、编辑再保存也保留全部double。[Excel规格](https://support.microsoft.com/en-us/excel/excel-specifications-and-limits)、[浮点限制](https://learn.microsoft.com/en-us/office/troubleshoot/excel/floating-point-arithmetic-inaccurate-result)。本轮未改变用户可见类型策略，也未进行桌面Excel验收。

B120 check-only再次匹配冻结来源，仍pending／not-started、子进程0、数据库写入false，未启动正式验收。未连接Server-200、未执行DDL／业务DML／Docker或创建数据库对象。B119原32表／目录租约已结束；B120冻结正式身份验收仍待授权，不借用旧授权。真实持久化、全部复杂关系／文件事务、正式身份／浏览器／跨端、同条件性能及MySQL首版总门槛继续，不因A28固定子集完成而关闭。