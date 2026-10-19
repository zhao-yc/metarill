# 已有独立应用的本地源码更新

本功能为 CLI 的本机源码更新流程。首期仅支持 `FullApplication`，不执行 DDL、启动服务、安装依赖或更新远程工程。平台三个旧按钮尚未通过本功能获得任意目录写权限；这里要求开发者明确传入本地目标目录与独立候选包目录。

## 先准备可信候选

使用与本次 CLI 对应的 Metarill 源码和元数据，在目标目录之外重新执行现有 `package` 命令生成候选。候选可以由另一份完整源码检出生成，不依赖旧工程或本机某个固定绝对 `source-root`。

```powershell
metarill package --metadata 新元数据.json --output E:\候选\应用下一版 --namespace Example.Application --source-root E:\Metarill源码
```

候选与目标必须是不同项目目录，不能互相包含。项目 ID、命名空间和数据库品牌必须一致。候选必须保持导出后的原始文件，不能提前填入私有配置或编辑手写壳；应用后在目标的手写区域维护业务。

现有三个清单都使用版本 1：`.metarill-package.json`、`backend/generated/.metarill-generation.json`、`frontend/.metarill-frontend.json`。工具核对三层归属、实际 SHA-256、元数据规范摘要、物理文件模型组合、两份前端生成片段以及候选 H5 静态字面导入和路由。候选的全部受管生成代码必须与当前 CLI 依据候选元数据在内存重新渲染的结果一致。未知版本、无法识别的归属或缺失依赖都会拒绝。FullApplication 的前端入口（含 main.js/App.vue/index.html）、宿主入口和四个基础工程等固定启动集合必须同时存在且登记；不能通过删文件和删两层清单来伪装成可选资产移除。

这些清单没有框架签名或独立运行时 ABI 版本。摘要验证用于检测文件漂移，不能证明候选来源可信，也不能替代代码审查、编译和实际验收。只使用开发者自己审阅并导出的候选，不把任意第三方目录当作可信升级包。

## 三个范围

| 动作 | 本次更新的受管内容 |
| --- | --- |
| `generate-project-config` | `backend/metadata/project.json`、前端生成配置、`pages.json`、两份 `pages.generated.json` |
| `sync-runtime` | 四个基础项目的受控源码/工程/许可、共享前端运行时与固定组件资产、独立宿主程序及依赖声明、`backend/metadata/files.json` |
| `generate-all-page` | 全部表级后端受管产物、页级 DTO/VO/Controller、页面、写入 schema、能力清单；缺失的 Service 手写壳仅首次创建 |

三个归属清单由更新器按实际变化合并并最后写入，保留范围外的基线记录，不直接用候选整包清单覆盖。配置变化可能同时影响页面、DTO/VO 或公共运行时；若候选在未选择范围仍有受管变化，预览会给出 `requiredActions` 且 `canApply=false`。必须重新预览并显式选择所需动作，不会静默扩大范围。在依赖闭包全部满足后才更新全局元数据摘要。

旧 v8 的项目配置动作仅处理路由与配置导出，全部页面动作没有自动覆盖路由；此处保留这一范围边界。首期采用保守闭包：无法证明可拆分的候选差异必须同时选择，而不是推测兼容。

## 预览与应用

```powershell
metarill update preview --target E:\应用 --candidate E:\候选\应用下一版 --actions generate-project-config,generate-all-page --plan E:\更新计划.json
metarill update apply --target E:\应用 --candidate E:\候选\应用下一版 --plan E:\更新计划.json --confirm 预览输出的planSha256
```

预览只读取目标与候选，显式计划文件必须在两者之外且不存在。计划包含 create/replace/delete/preserve、所属范围、前后摘要、完整目录指纹及确认摘要。返回码 0 表示可应用，3 表示依赖未满足，1 表示检查失败；错误不会开始源码写入。

应用重新读取两个目录并重新预览，只有结果摘要与确认的计划完全一致才写入。计划生成后，无论修改受管文件、候选文件或已登记的手写文件，都必须重新预览。没有 `force`、跳过冲突、SQL、安装或服务执行开关；未知、重复或缺值参数均拒绝。

## 手写与冲突保护

- 已存在的 `backend/generated/Services/*Service.cs` 逐字保留；`*.g.cs` 仍受管。
- AppID 所在 `frontend/manifest.json`、手写 `frontend/common/application.config.js`、示例配置和 README/忽略规则整文件保留。前端须维持 Vue 3 和字符串 AppID。
- 未登记的私有配置、其他手写 partial、前端自定义页面及构建目录不在写入集合。
- 受管文件缺失或被修改、一个新增路径已有非归属文件、重复路径、大小写冲突、越界路径、Windows 保留名/ADS、目录祖先链接或清单不一致都会拒绝。
- 删除已归属页面会同步删除两份生成片段并移除相关清单记录。首期拒绝删除表级类型，避免手写 partial、宿主或外部调用变成孤儿依赖；须先单独设计该项迁移。
- 首期明确拒绝 `BackendOnly` 目录、跨项目或跨数据库品牌更新，以及未知清单/生成器协议。

生成器和更新器共享 `backend/generated/.metarill-generation.lock`，更新器另持根目录锁。同一项目的协作生成会被互斥；普通 IDE 不遵守该锁，因此应用和恢复期间应停止其他编辑。工具在写入前重复校验摘要，但不宣称提供跨文件系统事务或防止任意非协作程序同时替换目录。

## 失败恢复

完整已确认 `plan.json`、备份、新内容、旧身份清单与 journal 会先刷盘到 `.local/metarill-updates/transactions/<事务ID>`，随后登记 `active.json`，再修改源码；三个清单最后提交，外层包清单排在最后。每个文件使用同目录临时文件刷盘后替换。完整成功后保留 committed 回执并清除 active。

普通异常会按逆序恢复本次字节，保留 rolled-back 日志及备份。回滚前及每次恢复写入前都重新检查：文件只能是原始字节或本次结果，第三种内容保留现场。回滚未完成或进程中断留下 active 时，任何新的预览/应用都会拒绝。

```powershell
metarill update recover --target E:\应用 --transaction active中的事务ID --confirm active中的planSha256
```

恢复先重算持久化计划的确认摘要，再逐项匹配 journal 的写入集合、顺序、动作、范围和前后摘要，新增文件也不可换成其他未登记路径。恢复同时绑定本机目录及旧 package/generation 身份备份，核对项目、命名空间和旧归属，并用原计划保留项/缺失壳状态重建旧目录指纹；仅补建手写壳而清单不变时同样绑定旧身份。不会读取候选或执行日志内的命令。缺少原计划的旧版事务日志不自动恢复；备份损坏、计划或日志被改动、路径未知、第三种用户内容或身份不一致时拒绝覆盖，并保留现场。若 committed 日志仅因中断尚未清除 active，核验全部最终字节后只清理状态，不回退已提交更新。不要删除 active 绕过恢复检查，也不要把 `.local` 备份提交到公共仓库。

更新后仍需按本项目既有流程进行代码审查、后端构建、前端依赖安装和 HBuilderX/实际应用验收。此源码命令不代表数据库结构已同步、前端已发行或任何数据库/设备验收通过。
