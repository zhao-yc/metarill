# 原前端 Android 独占模拟器启动验收（2026-11-01）

## 结论与范围

**本轮未通过原生启动，不能标记 Android、登录或业务验收完成。** 原平台前端在HBuilderX 5.34实际编译成功，60个声明页与60个实际定义逐项匹配，87个产物文件已固定；499份Git来源文件在执行前后逐字不变。标准调试基座APK实际安装成功，启动后捕获Weex原生桥接异常；未取得原登录页运行成功的证据。单页诊断对照没有完成设备连接，因此尚未证明异常只来自SDK、ABI或Metarill中的任何一方。

本轮没有修改产品、元数据、原前端或生成模板；没有连接MySQL，没有有效账号密码登录、CRUD、文件/Excel、关系、权限或性能成绩。模拟器证据与物理真机、Metarill独立APK/签名及最终验收分别记录。

## 已固定的实际环境

- 产品/原前端来源：`1fd2ef5534d959ece8c2349eb2d53655fc7b70d2`。从Git归档复制全部499份前端来源到本轮私有目录，依赖独立复制；没有在用户源工程构建或覆盖生成。
- HBuilderX：已有`D:\HBuilderX\HBuilderX.exe`，CLI `5.34.2026102909`。工程为原uni-app Vue3，AppID沿用用户提供的`__UNI__CF80CAE`。
- SDK：现有`D:\Android\Sdk`，现有API35 Google APIs x86_64系统镜像。独立AVD `Metarill_API35_d0338f4830ea`，独占`emulator-5556`；2核/2GiB、无窗口/音频/快照，最终用SDK支持的host GPU启动并核对`sys.boot_completed=1`和实际AVD名。
- 原有`emulator-5554`属于SimDock，前后台进程和前台`link.simdock.dev`执行前后保持，未向它安装基座或启动Metarill；既有HBuilderX和共享ADB未关闭或重启。
- 经典uni-app标准基座：原`D:\HBuilderX\plugins\launcher\base\android_base.apk`，104382406字节，SHA256 `ea5d30c4cf09628da1c19b6bc20061ecfbf85664382cdfc833158d9526fae755`。设备实际安装APK独立SHA相同，包`io.dcloud.HBuilder`、版本15.34/code1534、实际primaryCpuAbi=arm64-v8a。APK含arm64-v8a/armeabi-v7a/x86，设备为x86_64/arm64-v8a；这些读数本身不能证明崩溃原因。
- 仅作未认证接线准备的C#宿主来自`3e0b1b1`固定完整包，Application.Host.dll SHA256 `a9bc2d7e7a25351c6c96b045f8537012f4696b1076f2073ec29d3461a9d403cd`，Production/Web、开发认证关闭、无生效appsettings且连接为空。宿主直接HTTP公开Cap配置200/R200，只证明宿主准备；未据此宣称Android已请求成功。

## 实际执行及失败

1. 先只读检查HBuilderX/ADB及当前设备，发现SimDock模拟器，另建私有AVD和独立前端副本。每次启动前检查专属5556/5557端口，API仅独占8088回环。
2. HBuilderX实际命令如下；`--ui true`先返回“命令已下发”，随后通过精确项目/设备的`logcat`及设备安装/进程状态核对，不能以CLI退出0当运行成功：

```powershell
D:\HBuilderX\cli.exe project open --path <本轮原前端副本>
D:\HBuilderX\cli.exe launch app-android --project <本轮原前端副本> --ui true --deviceId emulator-5556 --playground standard --native-log true
D:\HBuilderX\cli.exe logcat app-android --project <本轮原前端副本> --deviceId emulator-5556 --mode full
```

3. 实际日志依次包含编译成功、安装成功、手机端文件同步成功、启动基座及崩溃记录。编译60页/87文件完整核对，基座安装和实际ABI/SHA另从设备读取。
4. 首次原生PID3780的AndroidRuntime报`java.lang.UnsatisfiedLinkError`：`WXBridge.nativeResetWXBridge(Object,String)`没有实现，调用栈为`WXBridgeManager.restart → WXSDKEngine.restartWeex → WeexInstanceMgr`。日志同时有libweexcore.so加载ok，不能仅根据异常名称推断为文件缺失、ABI不支持或C#接口错误。未取得原登录页运行成功的证据，崩溃原文/进程日志和截图均保留。
5. 只建立私有单页诊断工程，同一manifest逐字复制、同一实际安装APK，不替换原前端或交付模板。首次对照编译成功，但ADB反向创建`error: closed`且无应用PID；关闭该对照项目后，单独复核同设备8088反向映射已恢复，再运行一次，仍停在同样连接阶段并明确“已停止运行”。两次都不是运行通过，也没有证明单页出现相同Weex崩溃。旧失败不改写为通过。

## 准备阶段的保留失败

- 软件GPU模式在本机headless运行时报告libGLESv2/libEGL加载错误；当前SDK帮助支持swiftshader，但实际仍失败。给本进程增加SDK DLL目录后Python加载库成功，QEMU仍失败，不算修复。最终host模式实际启动Android。没有替换系统DLL、修改SDK或持久PATH。
- 曾只按`qemu-system-x86_64.exe`核进程，遗漏`qemu-system-x86_64-headless.exe`；实例锁独占打开失败后才按锁内PID、父PID及完整私有AVD路径定位真实子进程。修正筛选为全部qemu/emulator变体，确认/停止自有进程后才继续；未凭锁文件或主进程退出宣称所有子进程已停止，没有删除仍被持有的锁。
- 准备校验误把不会加载的appsettings.example.json当有效配置；Python前置失败后PowerShell误继续启动了本轮API。该宿主按PID/UTC启动ticks/独占DLL路径核对后立即停止，尚未发出测试请求；示例连接实际为空。随后只检查真正生效的三个配置名、显式检查原生命令退出码，重新核固定DLL/空连接后才启动准备宿主。原失败日志保留。

## 收尾与剩余

两个私有HBuilderX工程已关闭；API按PID/启动时间/完整DLL路径停止，模拟器按实际AVD名正常关闭。随后独立核对wrapper/headless/crashpad无本轮残留、ADB仅保留原SimDock设备且前台仍为SimDock。保留AVD配置、固定源码/产物摘要和原日志后，在解析绝对路径、验证目录属于本轮且无重解析点后删除专属AVD及临时用户/模拟器目录，包括不再使用的短期测试密钥。没有清理其他设备、进程或项目数据。

原始证据在忽略目录`E:\git\metarill\.local\android-native-20261101\d0338f4830ea`：`native-smoke-result.json`、`original-frontend-compiled-proof.json`、`installed-base-proof.json`、原/对照HBuilderX日志、原生崩溃和进程日志、截图、准备失败记录、AVD配置及独立收尾记录。结果passed=false。

后续需先解决此精确调试运行链或使用独占ARM物理设备/适配基座复验，并验证原登录页实际请求、有效登录及完整业务；不能跳过原界面、替换控件、拿单页诊断程序冒充Metarill，或仅因编译与安装成功勾选V06。iOS本轮查询仍只有空白输出，不算设备不存在或运行证据；MySQL首版、跨端与性能总验收继续未完成。
