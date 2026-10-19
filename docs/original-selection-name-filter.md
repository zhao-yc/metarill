# 原选择页名称筛选接线

## 固定合同

原列表SelectPage/MultiSelectPage仍提交表级辅助名称键，原页面与控件不改。生成器复用SelectionNameFilterCompilation的固定来源/存储列计划：当前只支持本页可读物理字符串、已证明冗余名称，以及既有安全Multi子集；非冗余、名称投影缺失、任意附加SQL、页级nameField改变稳定存储语义等继续明确为缺口。

标量名称通过StringNameQuery使用列LIKE，保留原物理列比较规则；JSON名称数组通过Metarill.Data.Queries.JsonArrayNameQuery按解码后的字符串成员做字面子串匹配，不把序列化JSON当普通文本。所有值作为参数，目标列来自已编译实体属性；请求不含可选择的表/列/SQL。JSON方言当前仅MySQL。沿Data已经验收的损坏历史值策略：非法JSON、非数组或包含非空嵌套结构的文档整行不匹配；平坦数组内数值/布尔/null不当作名称；空嵌套容器不引入递归字符串。

Query是唯一接线处，page、count、export共用：先应用原项目/父/软删除范围及名称条件，再排序/SQL分页或导出上限。已有独立来源ID筛选保留。只搜索本页已可读的本地冗余名称，不新增实时跨表读取或目标名称回填传播能力。

## 缺口与冲突

名称键和另一普通开放筛选键或缺口来源冲突时，所有同名绑定从可执行集合移除，生成唯一固定拒绝case，不依元数据顺序选中，不回退普通JSON文本LIKE。初始null/空白名称条件仍忽略；非空缺口请求明确中文错误。成功名称条件只接受字符串，数组（含空数组）、数字和对象均拒绝。

同一Gap写入generation-capabilities.json和生成警告，完整应用包在原统一门禁拒绝。显式BackendOnly仍可生成/编译其他能力；这不表示缺口接口可用。无DDL或前端迁移，更新Data/Generation后重新生成即可，保护手写partial。

## 实测比较规则与边界

原v8 JavaService.java:1517-1534对冗余SelectPage/MultiSelectPage生成辅助名称列的`lqw.like`。直接用FreeSql Contains会改变重音比较；JSON_SEARCH的原生比较也不能假定等同旧列LIKE。修补后标量直接列LIKE，数组模式使用`CONCAT(LEFT(固定实际列,0),@pattern)`从该列继承排序规则，不读取运行时元数据、不猜默认值、不接受客户端collation，且仍只在解码后的成员中匹配。

Server-200 MySQL8.4.11同四行Cafe/cafe/Café/CAFÉ的参数化列LIKE基线与生成HTTP结果逐项比较：

| 实际两列排序规则 | cafe | Cafe | Café |
|---|---|---|---|
| utf8mb4_unicode_ci | 四行 | 四行 | 四行 |
| utf8mb4_bin | 仅cafe | 仅Cafe | 仅Café |
| utf8mb4_0900_ai_ci | 四行 | 四行 | 四行 |

标量与JSON成员均与上表对应的本列LIKE基线相同。测试只改变本轮随机表两列，完成后精确清理。官方[JSON数据类型](https://dev.mysql.com/doc/refman/8.4/en/json.html)说明默认JSON比较采用binary排序；[表达式排序规则优先级](https://dev.mysql.com/doc/refman/8.4/en/charset-collation-coercibility.html)解释列与字面值的优先级。本方案经实测保留列的大小写/重音规则，不能据此宣称其他字符集、MySQL5.7或其他数据库已验收。

**字面关键词是本计划明确采用的保护规则**：`!/%/_`先转义，不再把客户端关键词中的`%/_`当LIKE通配符。这与旧lqw.like直接包裹关键词的通配行为有意不同。JSON搜索也明确改为decoded成员ANY，不恢复序列化JSON跨成员/转义形式的误匹配。原页面和搜索交互保持不变。[JSON_SEARCH官方合同](https://dev.mysql.com/doc/refman/8.4/en/json-search-functions.html)说明模式及固定escape规则。

## 页级控件覆盖

名称compiler与原ListFilters按同一有效controlKind及filterControlKind优先级判断实际请求键。页级将Text/List改为SelectPage/MultiSelectPage，或改变选择控件种类时，当前缺少完整存储证明，明确写filter Gap并拒绝非空名称键；Full应用包拒绝，BackendOnly仍可生成普通ID分支。基础Select/Multi改为Text/TextArea/RTF时，原UI提交源ID/存储键，后端不额外开放名称入口；int64只接收合法字符串ID，非法文本拒绝。filterControlKind=CheckBox优先时不误宣称名称支持，尚无完整ID数组编译证明的关系仍列缺口。

## 验证边界

- 最后Data适配修订：`name-query-data-final-v1.trx`5/5零skip，含原23行/19查询的真实成员、shape、特殊符号、别名与参数回归。
- `name-query-collation-adapter-v1.trx`17/17零skip：16编译规则/生成/原Vue合同，1真实生成HTTP加MySQL。随机三表18条来源记录（含外项目、外父、软删），三collation的旧参数化LIKE基线、生成page/count/export、中文/Unicode转义、引号反斜杠、SQL文本及JSON成员边界均通过；Scalar特殊引号载荷仅有离线ToSql及共用参数规则证明，不把它混称为所有输入均逐项实测。
- 页级覆盖与原列表的最新离线矩阵`name-query-effective-kind-v3.trx`33/33零skip：六种Text/List→Select/Multi与反向Text覆盖均真实生成、引用Core/Data编译、真实Vue发键；实际生成私有Query离线调用验证合法长ID与非法文本拒绝。另四组CheckBox优先级（单/多选×有/无文本覆盖）保持Gap。该批只构建表达式，不连接数据库；没有新增共享库DDL。
- 接线初版曾扩大7类56/56通过；该回归早于Latin比较修正，不代替当前修订的完整Generation最终验收。

IPageExportService接收器只核真实生成Service投影，不称Excel字节或下载验收。Data原23行fixture按结构指纹/完整行/FK/trigger核验后清理，本片18行fixture仅三张随机独占表并反查已清；私有日志位于本克隆`.local/selection-name-query/`，不提交环境、连接串、日志、产物。最新33项包含前述17项中的16编译用例，不能相加；当前组合有39个唯一通过用例（33离线生成/原列表、真实HTTP/MySQL1、Data5）。初期v2矩阵唯一失败为负证明误把现有FormatException断成JsonException，实际非法文本已拒；更正异常断言及组合优先级后v3全过。此片不包含浏览器或全部数据库支持验收。
