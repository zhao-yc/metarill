# 原 v8 详情页复用

原始入口为 `backend/yzp/src/main/resources/template/v8/uni/detail.vue`，分支逻辑来自 `UniappService.GenerateDetail`（831—1720 行）及其同文件辅助方法。Metarill 的独立入口为 `OriginalUniappDetailRenderer.Render(model, table, page, children)`；本切片不修改共享 `UniappRenderer`，由主集成切换调用。

`original-detail.vue.tpl` 的整个 `<template>` 块与原模板逐字一致。字段、主子区和按钮占位符按原算法填充，保留原格栅、单个 `uni-forms`、分组 `v-show`、校验、移动导航/更多菜单、按钮位置、自定义动作和原整份 entity 请求。增加的内部边界只处理真实父记录、当前页权限、字符串 ID、请求/弹层迟到、未编辑富文本及保存后真实 ID，不裁剪原请求。

## 运行时及控件依赖

- 公共入口：`common/utils.js`、`common/project.js`、`common/yzp_runtime/yzp-runtime.js`。
- 原分组：`common/yzp_runtime/detailLayoutRuntime.js`；公共按钮可见性沿用运行时 `isButtonVisible`，多选内部复用 `multiSelectFieldMixin.js`。
- 原组件：`zxz-uni-data-select`、`yzp-popup`、`yzp-file-library`、`yzp-cascade-tree`；uni-ui 的 forms/forms-item/easyinput/nav-bar/data-checkbox/datetime-picker/file-picker，以及 picker/image/scroll-view。
- 富文本仍导航 `pages/main/rtf/rtf`，沿用 `sendRtf` / `onReceiveRtf`。
- 选择页和子页依赖元数据固定的 `pages/yzp/{table}-list-{pageId}.vue`；自定义控件仍依赖原 tag 对应源码，不提供替代布局或只读占位。
- 页面整体样式依赖原全局 `App.vue` / `uni.scss`，本切片没有另造 CSS。

## 能力边界与接入

- `List` 使用原 `findNameId{字段去Id/Ids}{稳定列ID}`、POST/formParameters 协议；子页只加明确的 parentId 必要 API 适配。C# 原协议兼容由独立后端切片提供。
- MultiSelectPage 仅启用既有编译器能证明的冗余子集，原 props/events 和确认关闭行为不变；未知父、隐藏 ID、名称缺列、非冗余等仍拒绝，不放宽打包能力门禁。
- MultiCheckBoxDatabase、CascadeTree、动态 formParameters 表达式、旧 Image/Upload/Video 上传端点及任意自定义动作的后端完整性，不能由前端移植推定已验收。原前端分支已移植，主集成必须继续使用现有能力门禁并逐项接后端，不用普通文本替换。
- 非冗余联接/审计展示/级联祖先按原前端只读分支处理，不生成校验或自动保存；这不证明 C# 非冗余查询计划已完成。
- 主题暂取原平台 `#0ab3ad`，主分支新增 PrimaryColor 后接真实项目值。

## 验证

专用 `OriginalUniappDetailTests` 使用真实生成 Vue、Vue3 编译和挂载，覆盖单表单 validate、校验跳组、整 entity/null/0/只读回显、RTF 未编辑、按钮确认和先保存 ID、取消/完整行/长 ID、父范围切换、权限继承和子页刷新不写父。外部 uni-ui/子页组件以公开协议受控替身提供，不能当作真实浏览器、真实后端、HBuilderX 或设备验收。最终截图/实际上传/数据库行为由主集成统一验证。
