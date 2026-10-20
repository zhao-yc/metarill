# 可信安全 Service partial 的生成与防漏接

本片只为内部受信安全管理模块生成三个必要 Service 扩展，不接默认 Full 发布、HTTP、初始化或在线助手，也不解除 Runtime A、三个 partial 的发布门禁或五个 Custom 组件门禁。后续宿主必须同时完成可信模型、协调器、目录及原页面接线；文件存在或后端编译成功均不能代替完整验收。

## 可信入口与首次创建

`CSharpGenerator.Generate(SecurityManagementModelSet, outputDirectory, rootNamespace)` 是内部入口，先由 `SecurityManagementModuleFactory.RequirePublishedForGeneration` 验证私有签发记录。`GenerationModel.TrustedSecurityServices` 只在该入口建立，保存完整冻结目录与正常命名空间；每次选择模板重新核对。业务 JSON、同形 Binding、同名业务表或普通公开 `Generate(MetadataCatalog, ...)` 均不能选择安全模板。

仅固定 ID 与名称同时一致的 `yz_manager`、`yz_role`、`yz_admin_type` 选择编译资源 `TrustedYzManagerService.cs.tpl`、`TrustedYzRoleService.cs.tpl`、`TrustedYzAdminTypeService.cs.tpl`。三原页 ID 分别为 `6031906822946816`、`1692532858529329154`、`1696799309006348290`，不新增目标项目 CRUD 页，不开放原先关闭的复制、导入。

生成沿用既有批次归属、原子写及手写保护。三个 `Services/*Service.cs` 仅首次创建；再生成逐字保留已有手写文件。可信 `.g.cs` 将 `BeforeSave`、`BeforeDelete` 声明为有显式 `private` 的必需 partial，缺文件或只有旧空壳会被真实 C# 编译器拒绝。已有用户实现需要保留业务代码并补齐匹配签名，不能自动覆盖，也不能凭 `File.Exists` 认定安全规则已接好。普通业务仍采用原可选 partial，不新增守卫依赖。

## 写入证明和原业务规则

三个可信生成 Service 构造时把守卫序列固定为只读快照，要求唯一的实际 `SecurityRealmMutationGuard` 位于首位。这个实例检查只防止误接线；每次新增、修改、删除在手写钩子前及最终写入校验前，仍由生成代码无条件调用真实 `RequirePrepared`，核同实际 Session 事务、身份、固定表、目标与操作的 pending 准备状态。普通列表被外部清空、缺守卫、错误顺序、重复守卫、另一事务或另一目标都不能变成写入许可。

账号顺序保持：真实安全准备 → 必需手写 `BeforeSave` 生成 PBKDF2 → 生成 `ManagerCredentialValue.RequireDigest` → 既有关系/必填/归属校验 → 协调器最终复核 → Mapper。省略密码不刷新摘要；即使人为清空账号钩子的方法体，生成摘要证明仍拒绝把原文写入。角色和后台类型保留平台现有规则：以原页面动作权和合并后目标后台验证，不增加平台 super 门槛，也不恢复额外内置角色删除禁令。

最终权限撤销、角色归属及引用检查继续使用既有真实协调器，不在模板复制授权逻辑。原同项目但不同后台角色仍由最终合并校验拒绝，失败不能写源行。

## 本片验证和未验边界

测试编译实际生成的三管理页程序集并通过实际 `AddGeneratedServices` 解析 Service；离线数据传输只替代 ADO/ORM，不替代真实认证、目录、当前读取、协调器或对象规则。覆盖伪造来源/快照变动、首次创建与手写保护、四种缺失/空壳编译错误、四种错误守卫构造、准备证明的身份/目标/操作/事务、守卫集合后改、账号真实摘要及省略保持、空方法体拒绝、最终跨后台回滚，以及普通编辑者沿原角色页权保存 super 属性。

这些是离线真实生成、编译与生命周期合同；没有数据库、HTTP、Main 启动、浏览器、并发或独立 Full 下发实证。当前可信模型仍有后台项目候选、原权限工作台及初始化按钮等待接线项，不能以本片关闭完整角色 Runtime Gap。三安全表的实体、Mapper、DTO/VO 仍由既有生成器生成，未手工补产物。

2026-10-20 自有普通 clone 定向 15/15、邻近完整集合 87/87 均零跳过并启用警告即错误；15 项已包含在 87 项中，不重复累计。邻近涵盖受信模型、普通生成、已有写守卫、凭据协议及原账号 Vue 3 挂载。首轮邻近 86/87 的唯一失败为本 clone 缺少 Vue 包，保留原 TRX 后通过 NODE_PATH 复用已安装 Vue 3.5.44 重跑相同集合；源码、运行时和构建输出仍取本 clone。更早的测试夹具编译/必填输入错误也保留日志，没有改产品规则规避。
