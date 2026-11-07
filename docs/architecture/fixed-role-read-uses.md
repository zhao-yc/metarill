# 固定角色选择回执与原导出用途

本片只接生成源码与用途读取投影；不下发安全宿主、不修改元数据开关、前端DOM、CSS、props或事件，也未完成新版本实库/RR验收。

## 原协议事实与纠偏

只读核对原YZP v8 `JavaService.java` 3342–3453：导出按 `exportMethod=list/edit` 选择网格/编辑列，单 `_id` 且有关系读取时用辅助名称；冗余表的 `_ids` 仍选择原物理字段。原 `YzpExportService.java` 86–105 将该字符串原样写单元格，不拼接逗号或转换为名称数组。因此原固定账号的角色导出仍是ID数组JSON文本，并非角色名称导出；此前用途缺口“ID不能冒名称”与此原实现不符，本片纠正该假设。空值为空单元格、空数组为 `[]`，长ID、顺序和重复位置保留。测试只在私有元数据副本打开导出，canonical账号导出/复制/导入开关均不变。

原 `UniappService.java` 2292–2297 的选择确认发送 `onPopSelect(selectedList)` 完整行数组后关闭。现有确认前同范围回读仍保留，固定角色名和后台名进入原页VO，不增新弹窗/按钮。所选记录先去重并保持首次顺序；角色数组内部顺序、重复项、空/缺名位置仍由 `RoleCheckboxValue` 保留。

## 用途与权限

- 候选入口仍沿原来源页list/create/update任一通过；其成功不能替代实际读取用途授权。
- 列表、详情、写回执、type=2确认分别携带生成器固定的来源动作；确认使用来源list，先授权再查源行。每批名称查询再核真实actor与同一来源动作，不要求目标角色/后台页的额外query权限。
- 导出首先核真实actor和来源export。角色列输出原IDJSON，无名称查询；后台单选列按来源export计算固定名称，不走通用候选的目标list门槛，也不以ID伪造缺失名称。
- 名称只查询ID/name，200项串行、最多5000个唯一标识；角色按真实后台所属项目与两级软删限制，后台名按真实项目与软删限制。不可见/已删/缺失名称为null，真实空字符串保持为空字符串。
- 固定账号selection与export SQL不读取密码/盐/历史摘要；selection不回传掩码别名。普通页where/count/export查询范围不变。

## 门禁和验证边界

仅去除固定图已证明的selection/export用途缺口。未知角色声明、虚拟/联接冲突、隐藏字段边界、安全Runtime A及平台五处CustomControl独立前端依赖门禁（由FrontendPackager单独检查）均保留；不因本片读合同完成而宣称Full可发布。

`FixedRoleReadUseTests` 实际生成并编译Service，真实FreeSql翻译SQL，内存行执行固定表达式（含相关EXISTS）；SQL翻译副本使用真实provider，内存副本保留原表达式，二者不混称真实数据库。另通过真实CurdBefore事件取得执行前SQL/参数并立即中止，连接工厂始终禁止Open。XLSX由真实导出器写入、ClosedXML重新读取单元格。尚未执行实库、浏览器或独立宿主下发验收。
