# 独立uni-app前端

本工程复用Metarill/YZP公共请求、菜单、FileLibrary、日期、搜索选择器、Tiny Cloud及uni-ui组件；没有平台配置页面、私有配置、原项目AppID或默认真实账号。后端只有显式Development开发认证，不能据此作为生产账号/权限体系。

1. 启动包内ASP.NET Core宿主，显式启用开发认证并自行配置用户名/至少12位密码；参数见包根README。推荐API地址`http://127.0.0.1:5080/api`。
2. 在`common/application.config.js`设置实际API地址；前后端分离/真机还需后端配置可信`Files:LegacyPublicBaseUrl`，不能把localhost用于手机访问服务器。
3. 执行`npm ci --ignore-scripts`安装锁定依赖。`npm test`检查路由、脚本、组件和本地依赖闭包，不替代HBuilderX构建或真机验收。
4. 在HBuilderX导入本frontend目录，自行申请并填写`manifest.json`的DCloud AppID及目标小程序AppID，再使用HBuilderX CLI/界面运行。发行和签名需开发者自己的配置，本包没有宣称已通过HBuilderX发行。
5. 使用开发者自行配置的开发账号登录→菜单→生成list/detail页面进行CRUD。登录态仅走请求头；服务端项目固定为包内项目，客户端不能切换为其他项目。

显式启用开发认证的Development宿主允许HTTP(S)回环Origin的不同本机端口进行预检（127.0.0.1、localhost、IPv6回环），不允许Cookie凭据跨域，也不会跳过登录。Development未启用或Production不默认开放CORS；其他Web域名应通过可信同源网关或自有宿主策略接入。

原Tiny Cloud交互通过服务端`/admin/sys/tiny-cloud/config`获取部署配置，不分发TinyMCE核心/旧中文包。需按自己的Cloud账户与域名授权配置，未配置明确显示状态；生成器尚未支持的控件仍以generation-capabilities.json和只读保护为准。未知/平台专用CustomControl不会静默生成占位宿主，FullApplication直接拒绝，平台可明确选择BackendOnly。

包输出目录必须为空，已有手写宿主/配置一律拒绝覆盖；再次导出到新空目录后按审查差异合并。`common/application.config.js`属于开发者可修改的宿主配置，不存凭据。

## 原前端壳与手写内容

首次完整包沿用原App、PC顶栏/左栏、移动端菜单/我的、keep-alive首页槽、修改密码页及240px/60px窗口配置。`pages/main/main.vue`只首次创建空业务首页，`common/project.js`只首次创建原自定义按钮扩展接口；业务首页没有可确认的元数据字段，不会猜测平台财务或配置搜索页作为业务首页。普通工程按受控能力关闭平台配置/设备项目切换及尚未接通的Agent入口，保留原节点和样式。

新工程和已有工程更新是两个边界：`sync-runtime`只更新明确受管的公共模块/组件；已有App.vue、main.js、主导航壳、首页、project.js、manifest/AppID和API配置保持原字节。旧简化壳或自定义工程应另导新包对照合并，不能把未覆盖手写壳视为自动恢复原视觉。当前主题仍沿用源uni.scss，业务自定义主题配置下发尚待接通。

原logo和六张tab图标按来源清单原字节保留，默认头像经用户明确授权重新生成，沿用原位置与大小，未复制带第三方水印的旧图。当前构建/Node证据不代表已完成原PC/移动视觉验收、小程序或真机验收。

开发改密页沿用原布局与成功反馈，实际以POST提交oldPwd/pwd，服务端也保留旧GET合同。成功后所有旧会话失效；下一请求须重新登录。开发密码摘要保存在后端宿主固定 `.local/development-auth/credentials.json`，只在首次实际改密时创建，仅当前运行用户可读写。它不在包清单，不应提交、上传或映射为静态资源；使用部署者自己的私有配置备份流程保管。损坏/缺失状态或更换配置用户名/项目不会恢复旧配置密码，应保留现场核查。此能力只面向显式DevelopmentBootstrap账户，正式认证需另接。
