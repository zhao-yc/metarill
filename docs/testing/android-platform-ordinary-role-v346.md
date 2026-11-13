# V346 Android 平台普通角色只读验收

2026-11-06，固定提交 `b02a7fef`，Server-200 MySQL 隔离双库，运行编号 `2ac9bd5ebf9d60fb`。原 uni-app 使用 HBuilderX 5.34 标准基座，Android API 30 x86 模拟器。后端、前端和验收公共工具与 V345 的构建源码相同，复用经严格发布的 Production 宿主，重新离线安装前端依赖。产品代码未变更。

| 验收项 | 结果 |
| --- | --- |
| 普通角色登录及授权范围 | 真实登录成功，工作区只有 Metarill，菜单仅显示授予查询的角色页。合成账号不是平台管理员，只有一个 `is_query` 授权。 |
| 原角色列表和详情 | 两个列表条目可见，打开合成角色详情并看到“超级管理员 否”。实际分页与详情请求均返回 HTTP 200。 |
| 只读界面 | 列表和详情未出现新增、保存、删除、复制及设置权限等写入控件。未发送角色更新请求。 |
| 原 App 退出 | 使用 Android 返回键回到主导航，从“我的”确认退出，回到登录页。实际 `exit_login` 返回 HTTP 200。MySQL 独立连接确认原 Web 会话已撤销，活跃会话为零。 |
| 数据与清理 | 两个项目行及业务库 13 张表的摘要一致，平台表 24 张。清理后独立核验双库、账号、所有权记录、Redis 容器及配置均不存在，转发端口可用。专用模拟器已关闭并清理其 AVD 目录。 |

V345 的项目配置页拒绝仍作为单独证据保留。原 Java 项目详情投影含 `mysqlConnStr` 和 `dbPassword`，当前 C# 项目配置由平台管理员访问。V346 用已支持页面授权的角色页验证普通角色。本轮覆盖模拟器的只读流程，其他配置操作、签名包和物理设备继续分别验收。

实际 App 启动命令

```powershell
D:\HBuilderX\cli.exe launch app-android --project E:\git\metarill\.local\android-platform-role-v346\frozen-source\frontend\metarill_uni_admin --deviceId emulator-5556 --ui true
```
