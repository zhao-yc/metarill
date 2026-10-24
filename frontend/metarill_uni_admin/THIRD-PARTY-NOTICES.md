# 第三方来源与许可

2026-10-19 核对；项目 Apache-2.0 不替代下列组件自身条款。

| 来源 | 版本或范围 | 许可 | 处理 |
| --- | --- | --- | --- |
| [DCloud uni-ui](https://github.com/dcloudio/uni-ui) | 导入目录内各 package.json 版本 | Apache-2.0 | 保留官方源码与 uni_modules/LICENSE.uni-ui；项目改动在来源清单中记录 |
| [Cap](https://github.com/tiagozip/cap) / cap-widget | 0.1.50 | Apache-2.0 | npm registry 核对，固定依赖，验证服务配置由后端下发 |
| [CryptoJS](https://github.com/brix/crypto-js) | 4.2.0 | MIT | npm registry 核对；上游已声明停止维护，兼容阶段仍保留既有调用 |
| [SheetJS CE](https://docs.sheetjs.com/docs/getting-started/installation/nodejs/) / xlsx | 0.20.3 | Apache-2.0 | 按官方方案保留 vendor/xlsx-0.20.3.tgz 原始归档，file依赖与lock固定SRI；来源摘要见 vendor/sheetjs-source.json，完整许可保留在归档及 licenses/sheetjs*.LICENSE，原read/utils导入API不变 |
| [Cropper.js](https://github.com/fengyuanchen/cropperjs) | vendored 文件头版本 | MIT | 保留 vendor/cropperjs/LICENSE |
| [zxz-uni-data-select](https://ext.dcloud.net.cn/plugin?name=zxz-uni-data-select) | 1.0.20 | MIT；DCloud 基底 Apache-2.0 | 作者发布页明确许可；共享 components 源头原样接回搜索、多选、禁用、清空交互，保留同目录 LICENSE、LICENSE.upstream、NOTICE.md |
| [TinyMCE Vue 封装](https://github.com/tinymce/tinymce-vue) | 6.3.0 | MIT | 固定 npm 依赖与锁文件，保留 licenses/tinymce-vue.LICENSE.txt；只接入用户自有 Tiny Cloud，不安装或分发 TinyMCE 核心及来源未确认的旧中文包 |
| [Tiny Cloud](https://www.tiny.cloud/docs/tinymce/latest/vue-cloud/) | 部署指定稳定 8/8.x 频道 | 各部署自身 Cloud 账户条款 | 服务端私有配置 ApiKey，认证后仅下发浏览器加载所需字段；中文及付费资源由该账户远程提供，项目 Apache-2.0 不替代 Cloud 账户授权 |
| uCharts / qiun-data-charts | 原平台组件 | 未确认 | 未复制 |

原工程的项目logo及六张tab导航PNG按用户提供的源码恢复，逐文件来源与SHA-256见下表；保留来源不额外授予商标权，也不替代第三方素材自身授权。原头像带有第三方水印，未复制；用户已授权新生成默认头像替换，沿用原位置/大小，原创产物来源与提示词由项目主线另行记录。其他背景、独立图标字体、钉钉 SDK、原AppID、证书或部署配置未复制。uni-icons 内自带字体随官方组件条款保留。

### 文本编码维护

`uni-pagination` 1.2.4 的继承西语资源 `i18n/es.json` 原使用 GB18030，现按原编码严格解码并保存为 UTF-8，文案不变。独立包受控文本统一 UTF-8（无BOM）和LF；字体及离线归档保持原字节。

### 原壳导航素材来源

来源为用户提供的 YZP 工程 `frontend/yzp_uni_admin/` 下同名文件，恢复时保持原字节。logo表示原项目品牌，项目使用者应自行确认品牌使用权；下表仅记录来源与完整性，不表示Metarill给予第三方素材额外授权。默认头像 `static/images/avatar.png` 为用户授权重新生成的独立产物，SHA-256为 `e4829fa7be2628c6a103b246bfa2a4205b6fbdb7479cb890bf55f11f85eef96c`，未复制带水印原图。

| 原工程相对路径 | SHA-256 |
| --- | --- |
| `static/logo.png` | `0ba2334220a5c2921b2cb4451686ac8d458660d6aed1580f9cd5a69f241afcdf` |
| `static/images/home.png` | `8d689e3db188ca990e98e1e97a3c8d4695c317de8b2db0e60f79c86504b12078` |
| `static/images/home-selected.png` | `83c12672442f5bf9e42015889b0bd92eb3efb01697909945136083c89bdd87af` |
| `static/images/menu.png` | `956582638fd2a54119d5d71999361bb0b1d69289862bb477fc63516b1934eb18` |
| `static/images/menu-selected.png` | `414e607ffc02299212d530108ee55c739d1fee0f56dea25d5684d82ee0fb74b0` |
| `static/images/my.png` | `57925af35265074b4a6ad7a7aa67d096b301668dcc156468ef95a4bf6970ab21` |
| `static/images/my-selected.png` | `abedbf5a314c29b975a4aa68f70dff9d71671946c36334023ff3ac2001a515f2` |
