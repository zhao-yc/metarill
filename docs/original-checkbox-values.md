# 原复选框0/1协议兼容

## 支持范围

原 main 的 `$checkboxRange` 使用数字0/1；uni-data-checkbox 用严格等值判断已选项。已发布字段的逻辑类型为bool、`ControlKind=CheckBox`时，生成DTO的字段级转换器接收原0/1及兼容的true/false，VO只输出0/1/null。普通bool字段、Entity与全局JsonOptions不改；int类型CheckBox仍使用原整数边界。页面只决定字段可读写，不通过任意Extra发明控件类型。

DTO继续使用Optional：省略不写，显式null或空白字符串主动清空。字符串仅接受精确`"0"`/`"1"`或空白；数字词元仅接受精确0/1，2、负0、小数、指数、大整数及精度舍入值、对象、数组均拒绝。分页/统计筛选使用同一解析规则；原空筛选数组仍表示不筛选。固定Agent DTO schema列明这些输入类型，仍保留字段白名单与必填ID，不开放任意JsonElement。

## 更新步骤

这是生成源与核心边界修补，无新表或物理字段变更，无需新增DDL。已有C#项目更新Core运行时后再生成DTO/VO/Mapper/Controller；保护手写partial。原前端组件及数字range不修改。平台与独立宿主均使用生成属性上的同一转换器，不依赖宿主额外注册。

## 实际验证

- `CheckboxBooleanContractTests`22项通过：纯规则、省略/null、非法数值、普通bool隔离，以及真实生成产物引用Core/Data编译并反射实际DTO/VO。
- 该生成测试将实际VO交给真实Vue3挂载的原uni-data-checkbox脚本/模板，验证严格匹配、change发出数字、整entity保存及数字回显；测试网络受控，不称浏览器验收。
- `GeneratedOriginalPageApiMySqlTests`1项在Server-200明确隔离业务库通过：实际生成HTTP加MySQL，原整entity CRUD/候选/双父/排序/不分页/日期合同保持，新测0/1新增和保存、回读、page/count筛选、清空/省略、非法值拒绝且持久结果不变、普通bool仍拒数字。仅本轮随机三表，finally精确清理并反查无表。
- `LegacyPageSchemaTests`1项通过：真实MVC发现保留固定DTO、Optional、普通boolean及CheckBox输入枚举。
- 含原详情/列表真实Vue与生成合同、日期规则的局部回归58/58、零skip；该计数含前述22项，不能重复累加。另有上述MySQL1项和Agent schema1项，合计60个唯一通过用例。

私有证据在本开发克隆`.local/original-checkbox/checkbox-generated-http-v1.trx`、`checkbox-agent-schema-v1.trx`、`checkbox-original-ui-contract-v1.trx`，不提交日志/连接配置/产物。生成测试v1是Fact参数编译错误，v3/v4分别是测试夹具寻找旧文件名和假设页面Extra支持控件种类的错误；修正测试合同后真实原组件及实际后端通过。此切片未重跑完整Generation，最终全量与浏览器验收由主线统一执行。
