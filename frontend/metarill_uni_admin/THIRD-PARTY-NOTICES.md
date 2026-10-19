# 第三方来源与许可

2026-10-19 核对；项目 Apache-2.0 不替代下列组件自身条款。

| 来源 | 版本或范围 | 许可 | 处理 |
| --- | --- | --- | --- |
| [DCloud uni-ui](https://github.com/dcloudio/uni-ui) | 导入目录内各 package.json 版本 | Apache-2.0 | 保留官方源码与 uni_modules/LICENSE.uni-ui；项目改动在来源清单中记录 |
| [Cap](https://github.com/tiagozip/cap) / cap-widget | 0.1.50 | Apache-2.0 | npm registry 核对，固定依赖，验证服务配置由后端下发 |
| [CryptoJS](https://github.com/brix/crypto-js) | 4.2.0 | MIT | npm registry 核对；上游已声明停止维护，兼容阶段仍保留既有调用 |
| [SheetJS](https://github.com/SheetJS/sheetjs) / xlsx | 0.18.5 | Apache-2.0 | npm registry 核对，保持既有导入导出协议；未将依赖树打入源码 |
| [Cropper.js](https://github.com/fengyuanchen/cropperjs) | vendored 文件头版本 | MIT | 保留 vendor/cropperjs/LICENSE |
| [zxz-uni-data-select](https://ext.dcloud.net.cn/plugin?name=zxz-uni-data-select) | 1.0.20 | MIT；DCloud 基底 Apache-2.0 | 作者发布页明确许可；共享 components 源头原样接回搜索、多选、禁用、清空交互，保留同目录 LICENSE、LICENSE.upstream、NOTICE.md |
| TinyMCE 及其 Vue 封装 | 原平台 8.3.2 / 6.3.0 | 原组件使用方式未确认 | 未复制源码、资源、API key；保留富文本入口及事件协议，编辑能力待接入 |
| uCharts / qiun-data-charts | 原平台组件 | 未确认 | 未复制 |

未复制原平台 logo、头像、背景、独立图标字体、钉钉 SDK、AppID、证书或部署配置。uni-icons 内自带字体随官方组件条款保留。
