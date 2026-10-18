# Metarill 管理端

保留 YZP 的 HBuilderX uni-app Vue 3 工程、窗口导航、配置页面、工作台和通用运行时；不转换为 Vite/CLI 工程。方法与关键逻辑使用中文注释。

## 本地开发

1. 在此目录执行 npm ci，再用 HBuilderX 打开本目录。
2. 为新项目配置自己申请的 DCloud AppID；manifest.json 不含旧平台标识。微信 AppID、原生签名同样由使用者配置且不得提交密钥。
3. C# API 默认 http://127.0.0.1:8088/api，H5 开发端口 5188；部署使用同源 /api。App/真机需按自己的部署环境配置可达地址。
4. 先检查已有端口和进程，再通过 HBuilderX CLI launch web --project 工程绝对路径 --ui true 运行；发布使用 publish web --project 工程绝对路径 --webTitle Metarill。HBuilderX 界面查看与停止运行。
5. npm test 运行本地行为测试。通过这些测试不代表后端联调或真机验收通过。

## 接入边界

用户已决定保留现有交互设计，待确认原组件许可后接入。选择器和富文本编辑当前显式标记不可用，不使用简化替代交互；原字段值和 sendRtf/onReceiveRtf 协议保持。详见 THIRD-PARTY-NOTICES.md。

生成页面由平台元数据和生成器维护；此批导入是旧页面ID、API外观和通用组件的脱敏基线，后续更新必须回到元数据/模板来源。

## 标准写入清单

先运行后端生成器生成 platform 与 demo，再在仓库根目录执行 npm --prefix frontend/metarill_uni_admin run sync:write-schema。脚本只读取 backend/generated/{platform,demo}/frontend/common/yzp_runtime/page-write-schema.generated.json，合并后写入管理端 common/yzp_runtime/page-write-schema.generated.json；不从页面实体猜测允许字段。任务独立克隆可显式传 --source-root 源仓库绝对路径，来源冲突会失败，重复执行字节稳定。

共享请求仅对清单内标准 POST add/update 裁剪提交体，不改实体、原交互、未知路由或省略/null语义。服务端继续严格拒绝未知字段；每次元数据/生成器变更后应重新生成并合并清单。此适配不表示被占位的第三方组件已获得许可或H5已完成构建。
