# 固定安全管理模块与双模型准备边界

本片只提供固定来源的模型准备，不接入默认 FullApplication、BackendOnly、HTTP、初始化或在线助手；普通业务元数据不能自报模块标记获得安全表 CRUD 能力。

## 固定来源

`tools/metadata/derive-security-management.mjs` 从已提交的 `platform.json`、`security.json`、`files.json` 派生 `backend/metadata/modules/security-management-v1.json` 及 `.sha256`。资产记录三个源文件字节摘要；运行 `node tools/metadata/derive-security-management.mjs --check` 复验，默认命令只更新这两个固定文件，不接路径、版本、后台或表名参数。

资产保留原账号、角色、后台类型三表69列、三页62页字段及原控件/布局/ID/数组顺序；另保留最小六菜单树、权限工作台 CUSTOM_PAGE 声明、原初始化按钮合同。原密码列空初值检查与 `is_edit_pwd` 开关默认值区分，不包含账号、连接或实际凭据。初始化按钮的原路由字符串仅作为元数据保留，不搬原 `init_admin` 元数据重建逻辑。

## 内部专用组合

`SecurityManagementModuleFactory.Compose(application, publishedManagementAdminTypeId)` 只消费固定嵌入资产与摘要；参数没有任意资源、版本或业务标记。后台必须是当前应用已发布且归属本项目的唯一条目；多后台只将三页和其最小菜单绑定到明确选择的一个后台，不复制多套页面，也不改变原业务页面/菜单的后台。

结果 `SecurityManagementModelSet` 提供独立副本：

- `Physical`：业务 + 文件三表 + 安全六表。示例业务为10表，安全索引和列只出现一次，普通业务页面不增加。
- `Published`：相同物理图，并附固定三页的原显示/控件目录。两模型的结构计划指纹相等；页面、字段、菜单、按钮的项目、后台及引用在返回前验证。
- `Binding`：固定模块名/字节摘要/目标项目/管理后台来源说明，不能作为业务 JSON 授权标记。
- `PendingAdapters`：准确列出尚缺的宿主接线。

后续可信生成入口应消费 `RequirePublishedForGeneration(models)`。它核对工厂私有签发记录，再返回内部冻结目录的深拷贝。伪造同形结果或 Binding 被拒；修改任一次 `Published`/`Physical` 副本不影响内部目录。不能把该入口用于绕过原 Full 发布检查。

通用 `MetadataCatalogComposition.MergePhysical` 保持原样，仍拒绝业务同名安全 CRUD。专用工厂先拒保留表名及跨类别固定 ID 冲突，再通过原严格物理合成，最后只附固定原目录；没有对外通用“可信”开关。

## 仍缺的实际能力

| 声明 | 仍缺的宿主适配 |
|---|---|
| 后台类型项目 List | 六安全物理表有 `yz_project`，但本模块不提供项目 CRUD 页；`RelationCompilation` 因无目标读取页不生成候选，不能造占位页或退化全库查询 |
| 原权限工作台 CUSTOM_PAGE | 保留原 `pages/main/set_role_permission` 声明，尚未下发工作台原页面与三接口 |
| 原初始化账号按钮 | 保留原确认与路由声明，尚未接安全的本应用初始化；不执行原危险元数据重建 |
| 原三安全 Service | 本片没有下发三手写 partial、Reader/Guard/真实身份协调或其发布接线 |

Runtime A、三个 partial、五个 Custom 组件、Agent 和原 Full 门禁均未解除。仅有该模块资产或后端可编译，不能称独立安全管理已可用；也不允许默认 Full 静默降级 BackendOnly。

## 本片验证

离线验证固定来源与摘要、两轮字节稳定、公开字段/凭据反例、三页原对象保真、单后台长字符串 ID、归属/冲突拒绝、签发快照隔离、两物理图一致、原 Core/Full 门禁仍拒，并真实生成编译三管理页后端。未连接数据库、未启动宿主、未执行结构/账号初始化，未作原页面浏览器验收。
