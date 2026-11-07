# 自有 Tiny Cloud 富文本

Metarill 保留原富文本页面、HTML 双向编辑、预览、工具栏和 `sendRtf`/`onReceiveRtf` 事件。按用户选择使用各部署自己的 Tiny Cloud，默认 ApiKey 留空；未配置时只显示中文提示并保留已有内容，不请求 Tiny Cloud，也不退回 `no-api-key`。

平台和独立生成宿主均通过共享媒体程序集提供 `POST /api/admin/sys/tiny-cloud/config`，先调用 `IRuntimeAuthorization.RequireActor()` 验证真实身份，再从 `Metarill:TinyCloud` 读取固定字段；不接受客户端指定密钥、频道或脚本主机。响应仅包含 Cloud 浏览器标识、官方 CDN 脚本地址、频道、中文语言和原有付费插件声明，设置 `private, no-store`，不下发数据库、私钥或其他配置。ApiKey 是 Cloud 浏览器加载标识，认证用户的浏览器可见，不能将它作为后端秘密或授权依据。

平台的 `backend/appsettings.Example.json` 与独立包的示例配置均保留以下空值。实际值只填写各自受忽略的私有配置，或配置环境变量 `Metarill__TinyCloud__ApiKey`；禁止提交实际 key。平台配置入口为 `METARILL_SETTINGS` 指向的文件；独立宿主按自身 README 的私有设置文件/环境变量启动。

```json
{
  "Metarill": {
    "TinyCloud": {
      "ApiKey": "",
      "CloudChannel": "8",
      "PremiumPlugins": []
    }
  }
}
```

`CloudChannel` 支持稳定版 `8` 或官方明确提供的 `8.x` 频道；开源项目固定 Vue 封装 `6.3.0`，编辑器核心由 Cloud 提供，不承诺远程频道字节稳定。`PremiumPlugins` 仅允许原工具栏已有的 `mergetags`、`checklist`；只有对应 Cloud 账户具备授权时才配置，声明本身不取得授权。账户域名白名单、额度、中文翻译及付费功能按实际 Tiny Cloud 方案配置和验收。

原 `styleselect/fontselect/fontsizeselect/font_formats` 配置按 TinyMCE 8 现行名称改为 `styles/fontfamily/fontsize/font_family_formats`，保持原字体、字号与样式交互。中文使用官方 Cloud 的 `language: zh-CN` 远程资源；没有分发来源不明的旧语言包。原图片菜单使用自定义 `images_upload_handler`，向原 `/admin/sys/upload/uploadImage` 发送 multipart，登录/项目只在请求头传递。只接收成功响应顶层 `location` 的 HTTP(S) 地址，失败或离开编辑页的迟到响应不能插入假地址；后台公开图片上传兼容由对应文件服务切片实现和验收。

许可依据为 [官方 Vue Cloud 文档](https://www.tiny.cloud/docs/tinymce/latest/vue-cloud/)、[Vue 配置参考](https://www.tiny.cloud/docs/tinymce/latest/vue-ref/) 和 [官方中文资源说明](https://www.tiny.cloud/docs/tinymce/latest/ui-localization/)。Vue 封装采用 MIT，版权与许可保存在 `frontend/metarill_uni_admin/licenses/tinymce-vue.LICENSE.txt`。没有打包 TinyMCE GPL 核心，Cloud 服务自身仍按部署者账户条款使用。

验证边界：配置测试、页面方法/生命周期与上传协议模拟不等于真实 Cloud 加载。当前 key 依用户要求留空，实际 Cloud 编辑、中文/付费插件和图片端到端操作待新账户配置后验收；不因此重复索取 key。HBuilderX 构建与浏览器验证由集成者统一进行，其他目标平台不得由 Web 结果代替验收。

编辑器退出时依据 TinyMCE 的 dirty 标记：只打开未编辑仍回传传入的原始 HTML，避免初始化规范化标签造成意外字段更新；实际用户编辑才读取最新内容。该行为已通过实际页面生命周期探针验证，不代表真实 Cloud 加载验收。
