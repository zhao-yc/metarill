# V280 平台 Android 模拟器登录和项目读取验收

2026-11-05，从 `250aab9` 独立检出原平台源码，按 Release 发布 C# Production 宿主，警告视为错误。原前端以锁文件离线安装依赖。验收前后比对 500 个前端文件、69 个宿主文件及元数据摘要，原检出跟踪的页面和元数据未发生改动。

| 环节 | 实际结果 |
|---|---|
| 设备与宿主 | HBuilderX 5.34 在独占 Android API 30 x86 模拟器上编译、安装并启动原平台。设备 APK 与本机标准调试基座 SHA-256 一致。设备经专用 ADB 转发访问本机 Production 宿主，数据库为 Server-200 上本轮独占的平台库和业务库。 |
| 初始化与登录 | Production 宿主通过 `--initialize` 初始化两个空库。临时平台管理员从原 App 登录页输入密码。工作区选择页显示默认开发设备、Metarill 和示例项目。 |
| 原菜单与项目读取 | 选择 Metarill，从原开发菜单打开项目页。列表显示两条种子项目、实际的平台库与业务库名称、MySQL 和 Metarill C# v1。打开平台项目详情并核对原模板版本控件及说明。 |
| 退出与实库核验 | 原「我的」页确认退出后返回登录页。登录、项目列表、项目详情、退出均各一次 HTTP 200，菜单、权限、设备及工作区接口均有 HTTP 200。独立 MySQL 核对平台管理员角色、两库 24／13 张物理表、一条已撤销 Web 会话及零活动会话。 |

主要命令为 `D:\HBuilderX\cli.exe launch app-android --project E:\git\metarill\.local\android-platform-v280\frozen-source\frontend\metarill_uni_admin --deviceId emulator-5556 --ui true`。脱敏节点、实际截图、HTTP 时序及独立核验保存在 `E:\git\metarill\.local\android-platform-v280`。

结束后用独立连接确认本轮双库、账号、归属记录及隔离 Redis 已移除。专用 HBuilderX 工程已关闭，模拟器、回环转发、专用数据目录及临时凭据均已复核清理。

本轮覆盖 Android 模拟器原平台管理员登录、项目选择、菜单、项目读取与退出。普通平台角色、配置写入、复杂关系与文件／Excel、独立签名 APK 和物理真机分别验收。此轮使用自举管理员和关闭 Cap 的隔离配置。
