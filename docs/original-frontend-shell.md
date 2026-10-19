# 独立应用原前端壳恢复

## 来源与边界

本切片对照原工作区 `D:\svn\yzp\frontend\yzp_uni_admin` 和 `RuntimeSyncService`、v8 `UniappService.GeneratePagesJson`。原运行时以公共组件/模块为覆盖边界，`main.js`、`pages/main/menu.vue`是createIfAbsent，其余App/顶栏/首页等由既有工程维护。当前完整包首次创建这些原壳，不能用新简化导航替代。

- 恢复原App窗口与设备检测、PC改密事件、左栏/移动菜单、我的页及keep-alive首页槽；请求/公共globals沿用原运行时。
- 保留240px左栏、60px顶栏、原custom导航、tab顺序、边框/背景和原六张图标。原首页没有已确认的homePageId等元数据字段；首次只创建空 `pages/main/main.vue`，原 `common/project.js`首次提供空扩展接口，不带平台业务。
- 普通业务标题来自受控项目配置。平台配置/设备项目切换节点保留原位置与代码，按 `capabilities.platformManagement=false`关闭；Agent入口同理以 `agentGrants=false`门禁，不能假称独立宿主已经具备Agent控制面。
- 新登录外观保持现原页，登录成功通知原顶栏事件。退出保持原确认，再真实POST撤销服务器会话；只有成功才清客户端状态。
- 改密页只将请求改为POST以避免URL携带密码；原布局、字段、校验和成功反馈保留。后端保留旧GET参数合同。

这不是原前端整体验收完成声明。列表、详情及协议兼容由其它切片恢复；本切片未执行HBuilderX、Web视觉、小程序或真机验收。源uni.scss仍原样下发，业务项目自定义主题配置尚待接通。

## 首次创建与已有项目

`FullApplication`只发布到空目录，反复导出得到稳定字节；已有非空目录一律拒绝覆盖。后续 `sync-runtime`仍仅更新受管公共代码，App.vue、main.js、index/main/top/left/menu/my/change_password、project.js、manifest/AppID、API配置与自定义logo属于保留区域。新版本恢复原壳不构成覆盖用户首页的授权。仅上述固定原壳骨架完全缺失时，可由明确选择的 `sync-runtime`首次补建；AppID/API配置、README和私有凭据不属于该补建范围。存在的字节一律保留，计划和恢复日志只接受create/no-before，不能借恢复替换或删除已有壳。create最终原子发布也不允许覆盖；最后预检后若出现第三方文件，应用拒绝并保留待人工核查的恢复现场，不删除后来文件，即使内容与候选完全相同。新journal为v3，固定plan文件索引数组登记已经持久确认的原壳create；Move成功且确认刷盘后才可自动撤除。发布后、确认前崩溃视为归属不明，recover保留该路径，必须先人工审查移走再继续恢复。没有新增服务器路径/旁路事实文件；v2旧事务只读取原本无protected-create的边界。

旧简化壳或自定义宿主需在另一个目录导出后对照原壳审查合并，不能因公共模块更新就宣称现有页面已恢复保真。尚有未完成源码更新事务时，先用生成该事务的CLI核对recover后再升级CLI；不跨版本猜测恢复受保护路径。

## 开发改密的实际能力

仅显式 `Development` 且 `Authentication:DevelopmentBootstrap:Enabled=true` 的固定账户可调用；正式Auth由开发者接入。初始密码仍由私有配置提供，登录本身不创建凭据文件。首次真实改密核验旧密码，生成随机盐PBKDF2-SHA256摘要并以600000次固定成本保存到宿主 `.local/development-auth/credentials.json`；项目/用户名绑定、仅当前用户权限、逐级链接拒绝、同目录刷盘原子替换。成功后本进程全部session失效，其他进程下一校验刷新摘要并撤销旧session。重启只接受新密码。

坏状态、目录存在但文件缺失、更换配置用户名/项目均明确拒绝，不能回退复活配置密码。私有摘要不登记到包清单/更新清单，不能被同步工具覆盖、上传或作为静态资源。真实使用者应按自己的私有凭据备份流程保管；本轮只在随机测试包使用随机临时凭据，未修改用户真实密码或创建长期凭据。

## 原素材来源

以下路径相对于原 `D:\svn\yzp\frontend\yzp_uni_admin`。保留用户提供源码中的导航/品牌素材原字节，仅记录来源和SHA-256，不另授商标或第三方分发权。

| 原路径 | SHA-256 | 状态 |
| --- | --- | --- |
| `static/logo.png` | `0ba2334220a5c2921b2cb4451686ac8d458660d6aed1580f9cd5a69f241afcdf` | 恢复原字节 |
| `static/images/home.png` | `8d689e3db188ca990e98e1e97a3c8d4695c317de8b2db0e60f79c86504b12078` | 恢复原字节 |
| `static/images/home-selected.png` | `83c12672442f5bf9e42015889b0bd92eb3efb01697909945136083c89bdd87af` | 恢复原字节 |
| `static/images/menu.png` | `956582638fd2a54119d5d71999361bb0b1d69289862bb477fc63516b1934eb18` | 恢复原字节 |
| `static/images/menu-selected.png` | `414e607ffc02299212d530108ee55c739d1fee0f56dea25d5684d82ee0fb74b0` | 恢复原字节 |
| `static/images/my.png` | `57925af35265074b4a6ad7a7aa67d096b301668dcc156468ef95a4bf6970ab21` | 恢复原字节 |
| `static/images/my-selected.png` | `abedbf5a314c29b975a4aa68f70dff9d71671946c36334023ff3ac2001a515f2` | 恢复原字节 |
| `static/images/avatar.png` | `8bebe539d27a1d49d3fa742aa74d858bbdb72245b4a6c7d655413367c82175e3` | 原图第三方水印，未复制；用户已授权另生成替代 |

用户已明确授权重新生成默认头像。主任务生成的新PNG摘要为 `e4829fa7be2628c6a103b246bfa2a4205b6fbdb7479cb890bf55f11f85eef96c`，沿用原头像位置与大小，不复制/编辑带水印原图；新产物及提示词来源记录由主任务独立提交。此任务只将该既定产物纳入原壳闭包，不宣称浏览器视觉已验收。

## 原列表内部适配模块

完整包固定装配 `originalSelectionPageMixin.js`（原页面selectedList/isSelectAll/chkChange接口桥接）和 `originalListOptionsMixin.js`（固定选项端点的父/身份/卸载迟到隔离），继续保留原组件DOM与原按钮位置。模块源由主任务 `c71514c` / `ccf7e5c` 提交，本任务只新增装配清单与真实闭包回归，不复制未提交源或重复认领模块实现。

## 本轮验证

真实临时Full包验证了原App/左栏/菜单/我的全文与源副本一致、原顶栏/首页/改密style保持、Node脚本/动态导入/组件/路由/静态资产闭包、PC/移动生命周期与公共globals、原账户事件、真实服务端确认退出顺序。真实独立宿主HTTP验证匿名/旧密码/弱密码/未知重复字段、持久重启、全部会话和跨进程撤销、项目/用户名绑定、损坏/缺失状态、真实链接/断链与过宽ACL拒绝。临时包保护测试验证手写壳/API/AppID逐字节保留。

所有HTTP使用随机本机回环临时宿主并终止，不连接数据库，不执行DDL、不安装服务。测试日志保留在任务clone的 `.local/original-shell-results`，不能替代父任务的浏览器和设备验收。
