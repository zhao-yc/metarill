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

未复制原平台 logo、头像、背景、独立图标字体、钉钉 SDK、AppID、证书或部署配置。uni-icons 内自带字体随官方组件条款保留。
