# Metarill 管理端

保留 YZP 的 HBuilderX uni-app Vue 3 工程、窗口导航、配置页面、工作台和通用运行时；不转换为 Vite/CLI 工程。方法与关键逻辑使用中文注释。

## 本地开发

1. 在此目录执行 npm ci，再用 HBuilderX 打开本目录。
2. 本项目 AppID 为 __UNI__CF80CAE；Fork 或新项目应配置自己申请的 DCloud AppID，manifest.json 不含旧平台标识。微信 AppID、原生签名同样由使用者配置且不得提交密钥。
3. C# API 默认 http://127.0.0.1:8088/api，H5 开发端口 5188；部署使用同源 /api。App/真机需按自己的部署环境配置可达地址。
4. 先检查已有端口和进程，再通过 HBuilderX CLI launch web --project 工程绝对路径 --ui true 运行；发布使用 publish web --project 工程绝对路径 --webTitle Metarill。HBuilderX 界面查看与停止运行。
5. npm test 运行本地行为测试。通过这些测试不代表后端联调或真机验收通过。

## 接入边界

原搜索/多选选择器已确认 MIT 许可并按原组件接回。富文本按用户选择使用自有 Tiny Cloud，保留原编辑/预览和 sendRtf/onReceiveRtf 协议；key 暂留空时显示待配置且不加载远端脚本。配置见 docs/tiny-cloud.md，许可见 THIRD-PARTY-NOTICES.md。实际 Cloud 与多端验收仍待执行。

生成页面由平台元数据和生成器维护；此批导入是旧页面ID、API外观和通用组件的脱敏基线，后续更新必须回到元数据/模板来源。

## 标准写入清单

先运行后端生成器生成 platform 与 demo，再在仓库根目录执行 npm --prefix frontend/metarill_uni_admin run sync:write-schema。脚本只读取 backend/generated/{platform,demo}/frontend/common/yzp_runtime/page-write-schema.generated.json，合并后写入管理端 common/yzp_runtime/page-write-schema.generated.json；不从页面实体猜测允许字段。任务独立克隆可显式传 --source-root 源仓库绝对路径，来源冲突会失败，重复执行字节稳定。

共享请求仅对清单内标准 POST add/update 裁剪提交体，不改实体、原交互、未知路由或省略/null语义。服务端继续严格拒绝未知字段；每次元数据/生成器变更后应重新生成并合并清单。此适配不表示被占位的第三方组件已获得许可。H5 构建及只读浏览器验证的实际结果见 [验收台账](../../docs/acceptance.md)。

## Web 发行验证

2026-10-18 使用 HBuilderX 5.26 / Vue 3，实际执行 publish web --project 工程绝对路径 --webTitle Metarill --webHosting false，成功导出 unpackage/dist/build/web。manifest 显式设置 uniStatistics.enable=false，关闭编译器默认统计注入；当前发行产物不含默认统计SDK端点。

本机 launch web --ui true 仅下发命令、直接CLI模式也未启动预览端口，本次使用仅监听127.0.0.1的临时静态服务验证发行产物，不作为HBuilderX热更新预览通过。静态服务应挂载上述发行目录；localhost访问时API直连8088。
