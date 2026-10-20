# Metarill C#模板版本目录

项目原有版本下拉保留 `List`、`zxz-uni-data-select` 的位置、props、`id/name` 回调及原请求路径。首期只有 `Metarill C# v1`，稳定ID为字符串 `2300000000000000001`。目录由 `TemplateVersionCatalog` 维护，不建立版本管理表、CRUD页或动态脚本目录。

## 元数据与生成合同

平台字段 `1669606903854936065`（`yz_project.yz_yzp_version_id`）的 `controlDetail` 明确声明：

```json
{"catalog":"metarill-csharp-templates/v1","nameField":"name"}
```

编译器要求固定非联接物理来源字段、明确的`int64`逻辑类型和原 `List` 控件；虚拟列、`isJoinColumn`、同时指定关联表/页面及计算名称与物理字段冲突均拒绝。虚拟列即使已从物理模型过滤，也不能从Full门禁静默消失。仅这两个确切字符串键可用；未知目录、重复键、错误类型以及夹带 `table`、`tableId`、`whereSql`、`orderBy` 等键均不获得能力证明。详情、列表、用途读取IR与FullApplication门禁采用同一证明；不能仅添加一个Controller就把缺口隐藏。

原POST路径保持：

`/api/admin/auto/yz_project/6186303424888832/findNameIdYzYzpVersion1669606903854936065`

请求只接受有界的 `formParameters`、`parentId` 以及只读回显 `id`。`formParameters`作为旧页面不透明上下文保留，不参与身份、项目、目录或SQL选择。入口先执行真实页面权限校验，拒绝query及未知/重复body键；响应保持 `R<T>` 和字符串 `id/name/value/text`。显式只读 `id:null` 返回空候选，不退化为全部项。

写入仍走生成DTO/Service。创建和实际提交版本字段时只接受固定允许值，原Java ID（包括 `2104907095303213057`）、未知版本和显式null均拒绝。省略更新字段保持旧值，不顺带选择默认版本。生成的新增表单与新建Bootstrap项目默认v1；Bootstrap只补缺失项目，不能重置已存版本。

## 名称与旧项目空值

数据库保留版本ID，不创建 `yz_yzp_version_name` 物理列。VO从可信目录计算 `yzYzpVersionName`，列表与原只读文本使用该计算属性。原 `yz_yzp_versionSelectChange` 仍接收 `id/name`，原表单可以完整回传该名称。Legacy边界只剥离已经编译证明的这个只读别名，伪造名称不写入实体；任意其它 `*Name`、未知字段及直接向严格DTO提交名称仍拒绝。

已有Metarill C#项目的版本列可能为null。`MetadataRepository.LoadProjectAsync`保留原有C#读取兼容：不写库、不隐式选值，原实体VO仍回显null；原必填表单要求用户显式选择后保存。显式Java或未知ID会阻断加载生成元数据，不能默默按C#生成。此规则不承诺导入Java业务代码或转换旧数据库。

## 本轮验证与边界

本切片使用真实平台模型裁出的版本页，验证严格声明/用途IR/Full门禁、实际生成C#程序集、Service字段校验和Legacy已知别名剥离。另将生成详情实际编译挂载到Vue，派发原下拉change事件，核对字符串长ID、只读名称及已有null回读不被默认覆盖。真实FullApplication包以警告视为错误构建，在随机loopback端口启用显式开发认证，使用不可达的 `127.0.0.1:1` 测试数据库配置验证原候选POST、匿名拒绝和坏请求拒绝；不执行SQL、DDL或业务写入。

本轮不改运行中浏览器验收副本、不更新测试库元数据，不以离线Vue或本机HTTP替代HBuilderX/真实浏览器/生产认证验收。正式平台与前端可再生产物由集成方从合并源统一生成；已有数据库的 `control_detail`、默认值和提示文字需经原元数据发布流程显式更新，种子不会覆盖已有配置，不在启动时自动改写。

## 原UI保留矩阵

| 位置 | 本切片结果 |
| --- | --- |
| 原PC/移动详情表单、字段位置、布局、按钮 | 未修改共享Vue模板或样式 |
| 原版本选择器 | 保留zxz组件及v-model/localdata/filterable/disabled/dataKey=name/dataValue=id |
| 原事件与路由 | 恢复原`yz_yzp_versionSelectChange`名称，保持原findNameId POST和formParameters |
| 原只读版本文本 | 仍使用`readonlyDisplayText(entity.yzYzpVersionName)`，名称改由可信目录计算 |
| 原列表版本单元格 | 保留`relationDisplayText`及PC/移动行结构，读取同一个计算名称 |
| 新增与既有空值 | 新表单默认字符串v1；实际回读null保持null，不因新默认而覆盖 |

以上矩阵由模板/生成结果及真实Vue离线事件验证，尚不声称本切片已完成真实浏览器视觉验收。


## 原平台页面的默认值同步

保留的原项目详情页不能沿用旧 Java 版本默认 ID。`npm run sync:template-version --prefix frontend/metarill_uni_admin` 从正式元数据的项目页版本字段读取字符串默认值，仅更新原 `entity.yzYzpVersionId` 初始化。原下拉模板、事件、样式和其它字节保留；遇到重复字段、未知目录或不属于旧/当前目录的已有默认值时拒绝覆盖。该同步已接入 `scripts/dev.ps1 generate` 和 CI 的再生成核对，随后继续运行原写入清单及路由同步。它不回填数据库中的历史项目，也不改变既有记录的版本值。

## 页字段覆盖的虚拟列门禁修正

能力审计在物理列过滤之后仍按当前页真实 `config_page_column` 找回来源列，使用与目录解析相同的声明判断，同时检查来源 `controlDetail` 与该页覆盖 `controlDetail`。虚拟来源无论目录声明位于哪一处，都不能导出FullApplication；未知、损坏或冲突声明继续明确拒绝，不增加虚拟列或页覆盖独立目录的支持。未在当前页配置的来源列、其它页的覆盖不借给当前页。

定向回归使用真实平台版本页的裁剪元数据，先复现overlay-only声明漏拒，再验证实际Full导出在输出前拒绝、原有物理目录严格证明以及同页归属。重复JSON键仍由既有发布器提前拒绝，不能把该前置拒绝误报为新目录门禁生效。不改元数据、不再生仓库产物、不读写测试库，不代表浏览器视觉验收。


同步器也校验当前受支持的确切v1字符串ID、来源与页覆盖的固定目录声明；未知/原Java版本、数字化长ID、重复JSON键、联接列或冲突页覆盖均拒绝。不能因ID属于long范围而把它当成可用模板。今后增加版本时必须同时更新后端固定目录与此同步边界。
