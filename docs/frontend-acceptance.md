# 前端与 Agent 接入验收

## 已完成

- 独立 HBuilderX uni-app Vue 3 工程，默认 C# API 8088、H5 5188；未改成 CLI/Vite 工程。
- 仅导入通用配置页面及组件；项目详情的私有业务子表已排除。当前自举20页中的菜单ID2200000000000099000映射树形工作台，旧菜单双表CRUD不发布。
- 页面直接使用 yzAdminTypeId / yzAdminTypeName 选择后台归属；工作台沿用 snapshot / revision / kind 外观，由后端单表存储。
- 运行时行为测试158项通过；旧Java模板逐字对照和旧元数据迁移脚本测试没有迁入，C#生成器的一致性由其自身测试负责。
- tools/metarill-agent-cli 本地测试19项通过，入口语法检查通过；保留 X-Yzp-Agent-* 协议头、请求预览、凭据隔离、拒绝重定向、不自动重试写请求和任务恢复提示。

## 尚未通过

- 用户选择保留原选择器、富文本交互，待确认原组件许可后再接入；未复制未确认源码和原私有API key，当前两类能力显式显示待接入。
- HBuilderX 5.26.2026091802 / CLI 1.0.0.0 已导入工程。publish web 实际报“appid 不存在，请在 manifest.json 中重新获取”；空值和本地占位不获接受，已恢复空值，不复用旧或他人标识。launch web --ui true --compile true 只返回命令已下发，logcat web 无构建日志，不能算编译成功。需正式新AppID后重新执行。
- 尚未完成C#真实后端全接口联调、浏览器实际业务闭环、微信/Android/iOS真机、原生签名、性能对照；Node模拟服务测试不能替代这些验收。
- Agent CLI client_id 为 metarill-agent-cli，服务端须支持设备授权、身份核对、能力目录、撤权、刷新和任务协议后才可宣布可用。

## 交付验收顺序

新AppID与插件许可→HBuilderX Web编译→登录/设备项目选择→元数据CRUD→结构预览/同步→生成与独立项目CRUD→菜单/权限/文件/导入导出/Agent→Web及三类真机→200服务器同资源性能对照。

HBuilderX 自带条件编译预处理器与 Vue SFC 编译器检查了 74 个项目 Vue 文件，脚本与模板描述语法零错误；这仍不替代完整 H5 构建。官方组件源码按原样保留，未做项目格式化。
