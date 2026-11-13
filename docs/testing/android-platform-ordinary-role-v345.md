# V345 Android 平台普通角色验收

2026-11-06，固定源码 `7e7bae38`。结论为部分通过。

| 场景 | 实际结果 |
| --- | --- |
| 宿主和前端 | Production 宿主严格发布，原 uni-app 依赖离线安装。HBuilderX 5.34 使用 API 30 x86 模拟器和标准运行基座。首次使用宿主显卡启动时 ADB 离线，切换 SwiftShader 后设备可用。 |
| 登录、工作区和菜单 | 使用 MySQL 独占双库中的合成普通账号完成真实登录。工作区只列出获授权的 Metarill，管理菜单只出现“开发 → 项目”。 |
| 项目列表 | 点击“项目”发出 `POST /api/admin/auto/yz_project/6186303424888832/page`，HTTP 200，原 App 显示“无权执行此操作。”。夹具仅授予该页面的 `is_query`，未授予管理员身份。 |
| 详情及后续验证 | 列表被拒绝，详情、写入控件、退出及相关会话核验均未完成。本轮无产品和界面变更。 |
| 清理 | 执行器结束后以独立连接核验测试双库、账号和所有权记录不存在，Redis 容器和配置目录不存在，转发端口可用。本轮工程已关闭，专用模拟器、AVD 目录和基座 APK 副本已清理，模拟器端口无监听。 |

生成 Controller 在列表调用 `Actor("list")`。当前 `HttpRuntimeAuthorization.RequirePagePermissionCore` 对平台非安全配置页先检查管理员身份，再判断页面操作权限。项目页因此无法仅靠该角色的查询授权打开。下一步需要核对原 Java 的平台项目页权限及字段暴露边界，再确定普通角色使用的验收页面或所需兼容改动。

实际 App 启动命令

```powershell
D:\HBuilderX\cli.exe launch app-android --project E:\git\metarill\.local\android-platform-role-v345\frozen-source\frontend\metarill_uni_admin --deviceId emulator-5556 --ui true
```
