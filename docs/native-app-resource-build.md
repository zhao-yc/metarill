# Android／iOS App 资源编译（2026-10-29）

## 实际完成范围

冻结起点 `f5a250039fb5fc8266f7684e76dd01b92d77000f`，沿用原 uni-app Vue 3 平台前端；使用当前正规 C# CLI 从 demo.json 生成新的 FullApplication，再将平台及示例前端复制到本轮私有目录。原工程和旧固定验收包没有作为构建输出目录。HBuilderX 版本为 `5.33.2026102401`，用户提供的 AppID 为 `__UNI__CF80CAE`；仅在独立编译副本写入 AppID，未改公开配置的身份策略。

| 对象 | Android appResource | iOS appResource | 声明页／实际编译页 | 文件／字节 |
| --- | --- | --- | --- | --- |
| 原平台前端 | 编译／导出成功 | 编译／导出成功 | 60／60 | 每份87文件，5759916字节 |
| 当前生成示例 | 编译／导出成功 | 编译／导出成功 | 12／12 | 每份37文件，2941688字节 |

这是 App Vue 共用的 JavaScript／CSS／WebView 资源，不是两套操作系统原生二进制；四次命令分别实际执行，Android/iOS导出的对应资源字节相同。用产物 app-config-service.js 的实际注册目录及 app-service.js 的 `__definePage` 集合核对每个页面，并逐项固定文件SHA256。启动清单包含android/iPhone/iPad、AppID正确，启动HTML的本地JS/CSS引用和默认头像存在，浏览器专属Cap／裁切动态库未进入逻辑层。上述检查不能替代设备运行。

## 两个实际阻塞与源头修复

首轮CLI返回0，但日志明确“编译失败／导出失败”：平台登录页在App逻辑层静态引入浏览器用cap-widget，HBuilderX日志注入改写其内嵌Worker字符串后发生语法错误。将导入和浏览器求解代码限制在H5条件分支；Web仍使用原实例、进度、重置及token。非Web在配置关闭时沿原空token登录，若配置启用或读取失败则明确拒绝，不静默跳过验证。当前C#平台公开配置为enabled=false；非Web启用Cap仍需真正的跨端实现及验收，不能算已支持。

第二轮仍返回0，但日志指出IIFE不支持动态分包：图片裁切模块虽然运行时检查DOM，动态import仍进入App构建。将H5专属动态Cropper／CSS加载与运行环境检测按条件编译隔离；原H5按需加载／弹窗不变，非Web继续原文件上传，不因偶然存在同名window/document而进入DOM库。源头为原前端共享运行时，正规FullApplication打包已实际携带修复，不在生成包逐份补丁。登录HTML/CSS与两个H5条件编译后的可执行文本均与起点逐字一致，未改变原页面、样式、按钮或接口。

没有修改第三方库、锁版本、认证接口、元数据或数据库。依赖副本先逐项核对锁文件的非根依赖和直接依赖相同；首次把生成包不同的根名称也当作不一致的准备失败保留，修正的是验证断言，未改锁文件或安装版本。浏览器库的运行边界参见[DCloud官方模块说明](https://uniapp.dcloud.net.cn/tutorial/page-script.html)。

## 验证与复现

- 原前端完整测试300项通过、零失败零跳过；新增13项包含实际条件编译后的页面方法、配置失败／启用验证时禁止提交、Web原token／进度，以及非Web有同名DOM全局时继续原文件。
- 前端打包、可信管理原资产及独立Agent完整包相关合同29项通过、零跳过；平台／示例两轮正式生成215个文件逐字不变，含24个手写partial。
- 正规CLI严格构建零警告／零错误，先后两个真实Full包均成功；最后示例携带当前运行时修复。
- 平台／示例Web各实际编译并导出成功；平台微信60页编译并导出成功，未上传或托管。
- 原失败日志、构建结果、源码／产物逐文件摘要及回归记录在忽略目录 `.local/native-app-resources-20261029/978c239bfe444d4d9d402a1e15632d61`；失败不改记为成功。

在HBuilderX导入独立工程后使用实际命令：

```powershell
D:\HBuilderX\cli.exe project open --path <独立前端副本绝对路径>
D:\HBuilderX\cli.exe publish app-android --type appResource --project <独立前端副本绝对路径>
D:\HBuilderX\cli.exe publish app-ios --type appResource --project <独立前端副本绝对路径>
D:\HBuilderX\cli.exe publish web --project <独立前端副本绝对路径> --webTitle Metarill --webHosting false
D:\HBuilderX\cli.exe publish mp-weixin --project <平台副本绝对路径> --upload false
D:\HBuilderX\cli.exe project close --path <独立前端副本绝对路径>
```

检查明确的“编译成功／导出成功”及实际目录，不能只用CLI退出码。App资源位于副本的 `unpackage/resources/__UNI__CF80CAE/www`。本轮分别保留四份快照，不用后续同目录覆盖的产物冒充前一次结果。HBuilderX提示基座SDK需与5.33配套；小程序仍有img样式选择器和三处鸿蒙UA检测告警，以及未安装微信开发者工具的提示，未擅自改原样式或第三方组件。

## 未完成边界

HBuilderX启动后再次查询的真实设备列表仍为空；本轮两个独占工程已关闭，未停止用户其它工程或HBuilderX。当前没有连接的原生设备；没有APK／IPA、签名、Xcode／Gradle原生编译或设备安装，没有原生登录、CRUD、关系、权限、文件／Excel与网络配置验收，也没有执行新MySQL／SSH／Docker或性能负载。Tiny Cloud key仍按用户要求留空，不能声称原生富文本验收完成。V05／V06／V07、完整原界面回归和MySQL首版总验收均继续待办。本轮只完成V15明确的App资源编译子集，不合main、不部署生产。


## 2026-10-31 宿主环境复核

本次仅复核设备验收前提，没有重新编译、安装、上传或操作业务数据；V15 的四次资源编译证据仍以原冻结来源为准。

- 实际宿主 HBuilderX 为 `D:\HBuilderX\HBuilderX.exe`，进程自 17:11:57 已运行；复用原进程，未关闭用户应用。实际 CLI 版本为 `5.34.2026102909`。
- 受限会话不能正常看到宿主进程或访问 HBuilderX IPC，曾返回“未运行”；宿主只读查询已纠正，不能据此认定未安装或未启动。
- 按本机 `devices list --help` 的实际参数，分别运行 `devices list --platform android`、`devices list --platform ios-iPhone`、`devices list --platform ios-simulator`。三个输出均只有 CRLF（2 字节），没有设备标识或明确的空列表。设备清单尚未取得，不能把空白输出当作“已确认无设备”。
- 本机 `publish app-android --help` 仅列出 `--type appResource`。资源导出不能替代 APK、签名、安装或真机业务验收；Android/iOS 总项继续待验。

原始 CLI 帮助、版本、三平台查询输出保存在忽略目录 `.local/native-device-check-20261031`。本次没有创建新工程、启动项目服务或留下待停止的测试进程；原 HBuilderX 继续运行。


## 2026-11-01 独占Android模拟器实际启动失败

V27从1fd2ef5实际冻结499份原前端，HBuilderX5.34编译60/60页、87文件并安装固定15.34经典标准基座；原源码不变。独占Android15模拟器启动后出现WXBridge.nativeResetWXBridge的UnsatisfiedLinkError，未验证原登录页运行成功，单页对照又停在ADB反向连接阶段，原因尚未证明。基座安装/编译不代表Android运行通过，也不是Metarill独立APK/签名或真机验收。原失败、准备阶段的headless子进程核对修正及自有API/AVD精确收尾见[本轮实际记录](testing/android-emulator-native-smoke.md)。原SimDock模拟器未安装或运行Metarill，前台保持；本轮独占设备/临时数据已清理。V06/V07仍待验收。


### 2026-11-01 原平台Android x86未认证运行

V29已在独占API30 x86标准基座实际显示原登录页，验证空值提示、原三字段请求修正前400/后401及冷启动；499源码逐项保持，本轮环境及临时密钥已清理。V27失败、首次包发布拒绝与冷启动采集断言失败保留，具体证据见[原生专项](testing/android-emulator-native-smoke.md)。仅未认证登录子集，没有有效账号/业务、生成包设备运行、独立APK或物理真机，V06继续待验。

## V88 当前独立示例四端及宿主 HTTP

使用固定 86865b1 的正规 CLI 和源归档，从 demo.json 生成 651 文件 FullApplication。NuGet 从本机缓存恢复，包内 Application.Host Release 严格构建零警告、零错误。实际本机 Production／Web 回环进程以空连接启动，健康和公开 Cap 配置各返回 200。原三字段合成登录、菜单、权限及生成页详情／分页／计数／新增七项均返回 HTTP401／R401，无会话令牌。测试后关闭进程并核验端口停止监听。

用 HBuilderX 5.34.2026102909 串行导出四个编译副本。Web 52 文件，微信 169，Android 和 iOS 各 37。微信与两个 App 都逐项匹配独立应用的 12 个原页面。只有编译副本写入已有 DCloud AppID，微信未配置值在该副本由工具规范化为 null。正式包 389 个前端文件逐字保持，四个 IDE 工程均关闭。未上传、托管、签名或安装，真实数据库、登录和设备业务仍待验。

复核原始构建和实际 HTTP 证据位于忽略目录 .local/current-standalone-cross-platform-86865b1。

## V112 当前提交四端资源与设备复核

固定 `b380a63`。从 Git 归档核对原平台 499 份前端文件，再用当前 C# CLI 严格编译并正式生成 651 份文件的 FullApplication，其中 389 份前端文件与 V88 的生成包逐字一致。逐一核对 V87／V88 八个构建副本实际输入，仅保留原记录中的编译副本 AppID 设置及微信 AppID 空串转 null。未重新导出八套相同输入。

| 已导出产物 | Web | 微信小程序 | Android App 资源 | iOS App 资源 |
| --- | ---: | ---: | ---: | ---: |
| 原平台 | 156 | 457 | 87 | 87 |
| 独立 Full 示例 | 52 | 169 | 37 | 37 |

八个实际产物目录逐文件 SHA-256 与原记录完全匹配。复用运行中的 HBuilderX 5.34.2026102909。实际执行 `D:\HBuilderX\cli.exe devices list --platform android`、`--platform ios-iPhone`、`--platform ios-simulator`，均返回 0 且没有设备标识。用 HBuilderX 自带 `adb.exe devices -l` 独立得到 0 个 Android 连接。iOS CLI 的空白响应仅说明未取得设备清单。本轮未安装、签名、启动设备应用或执行跨端业务。V05／V06／V07 仍待正式运行验收。

## V113 当前独立包生产宿主回环

固定 b380a63 新生成的 651 文件 Full 示例。包内 Application.Host 使用本机 NuGet 缓存按 Release 严格发布，在清除继承配置后以 Production／Web 启动，仅监听本机独占回环。HTTP 验证前核对监听 PID。健康及公开 Cap 配置共 2 项返回 200；原三字段合成登录、菜单、权限及生成页面详情／分页／计数／新增共 7 项均返回 HTTP／R401，未返回会话令牌。退出后独立核对监听已释放。

实际发布日志、宿主日志和逐项响应状态保存在私有目录 .local/current-native-resources-20261103/production-current。本轮没有数据库配置或真实登录，不替代独立包 MySQL 权限、事务和设备验收。

## V122 当前普通与管理完整包严格发布

固定 `0808aad49c6a2b6347c7c61a41bc1dbffc4711ee`。核对生成 CLI 的后端和前端运行来源与该提交一致，从独立检出按相同示例元数据分别生成 FullApplication 和 FullManagementApplication。两包含清单共 651／724 个文件，清单列出的 650／723 个文件逐项重算 SHA-256 一致。两包各 18 个项目引用均指向包内源码。公开示例连接、Redis、Cap 密钥以及 DCloud 和微信 AppID 均为空。

两包以包内 Application.Host 各自执行 Release 严格发布，均产生 69 个文件。Security 第三方通知与对应包内源码逐字一致。以实际发布宿主启动 Production／Web 回环，只用不可访问的本地占位数据库地址，健康与公开 Cap 正常，匿名菜单、原业务分页／计数／新增返回 401；管理包另核对匿名原角色分页拒绝。两包分别完成 6／7 项检查并独立确认进程及端口释放。未连接真实数据库、提交真实账号登录、安装设备应用或运行性能负载。

## V123 管理完整包四端资源

以 V122 的 `0808aad` 管理完整包为源，399 个前端文件逐一保持。只在四个编译副本写入已有 uni-app AppID，微信 AppID 保持空配置。HBuilderX 5.34 分别本地导出 Web 75 文件、微信 241 文件、Android 和 iOS App 资源各 44 文件。

Web 的实际脚本包含全部 19 页路由。微信 app.json 与对应 JS／JSON／WXML、两个 App 的 app-service.js 实际注册均逐项覆盖 19 页，包括后台类型、账号、角色和权限页。四份产物逐文件摘要保留，四个临时工程均关闭。没有上传、托管、签名、安装或运行设备业务。
