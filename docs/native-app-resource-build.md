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
