# 原上传接口兼容

保留 `POST /api/admin/sys/upload/uploadImage`，用于现有 uni-file-picker、ImageUpload 和富文本上传协议。原 Java 端点调用 `importLegacyPublicFile`，因此成功文件仍按公开文件导入；上传身份和所属项目来自真实登录及项目授权，由 `IFileLibraryResolver.RequireCurrent()` 选择数据库。query 中的登录令牌、项目参数和表单中的路径不建立身份，也不指定磁盘位置。

## 协议与处理

只接受一个名为 `file` 的 multipart 文件，兼容原客户端附带的普通表单字段但不执行其中的路径或URL。成功响应为 `{code:200,data:{url,name,extname},location:url}`，URL来自服务端已保存的公开文件路由，使用同源 `/api/admin/sys/file-library/public-content/<id>`；多项目宿主带可信 `projectId` 定位提示。失败不返回伪造的 `location` 或地址。

JPEG/PNG真实解码并重新编码，剥离附带载荷及元数据，应用EXIF方向；压缩输入与处理结果均最多10MiB，且受更低文件库上限约束。单图最多1600万像素、单边8192像素，native解码并发最多2个，超过限制在分配大画布前拒绝。实际编码必须匹配扩展名，截断或损坏内容拒绝。该兼容入口只处理JPEG/PNG图片，其他图片类型使用专用文件库；普通PDF、Excel、文本等按文件库扩展名白名单和大小规则流式导入，不进入图片解码，不下载任意URL。

文件库默认总文件上限100MiB，HTTP请求上限为101MiB以容纳multipart头。计划、上传和完成共用同一个数据库事务，失败导入不留下无法继续的计划记录；数据库与磁盘无法提供同等原子提交，数据库提交结果不明时仍沿用保留随机对象供核验的策略。

## 开源依赖

采用 [SkiaSharp 4.153.1](https://www.nuget.org/packages/SkiaSharp/4.153.1) 和 [SkiaSharp.NativeAssets.Linux.NoDependencies 4.153.1](https://www.nuget.org/packages/SkiaSharp.NativeAssets.Linux.NoDependencies/4.153.1)，均使用MIT许可，中央版本固定。Windows原生依赖由SkiaSharp包引入；本次NuGet中的Windows/Linux第三方声明SHA-256一致。仅统一换行和行尾空白，许可证与native第三方声明保留在 `backend/src/Metarill.Files/licenses`，源码独立包、构建及publish均携带；不因包装库使用MIT就省略native组件声明。

无数据库的实际native JPEG/PNG、无效内容、像素上限与EXIF测试在Windows/Linux CI均执行；绿色编译不能代替动态库实际加载。其他CPU架构仍需分别实际验证。

## 验收边界

本轮Windows实际Files项目38项通过、0跳过，含5项native图片测试、9项公开地址规则测试和1项兼容导入事务/普通计划策略验证。整个API项目真实13项通过、0跳过。真实Server-200双库HTTP使用平台与业务库分别保存PNG/PDF，核对文件只出现在对应数据库及可匿名公开下载；伪图片、超限、禁用后缀、多文件、错误字段、匿名请求（包括仅query持有有效令牌）均拒绝。独立包打包/许可及真实宿主共10项通过、0跳过，实际包内旧上传PNG端点成功加载native并公开下载。最终Linux CI以协调者合并后的实际结果为准。

**引用生命周期边界：** `FileLibrary`字段的ID引用仍由事务观察器管理；旧 `RTF` / `ImageUpload` 只保存公开URL，观察器目前不追踪富文本正文或旧图片URL的引用。此兼容接口恢复上传/展示协议，不代表这些URL已获得与FileLibrary字段相同的引用阻删和解绑生命周期。组件许可、正式富文本接入和真实多端仍需各自验收。

平台使用 `Metarill:Files:LegacyPublicBaseUrl`，独立应用使用 `Files:LegacyPublicBaseUrl`；默认空保持同源API路径。跨源端口/真机必须配置实际可达的API公开origin，可带路径前缀，例如 `https://api.example.com/prefix` 会生成 `https://api.example.com/prefix/api/...`。只接受不含认证、query、fragment的HTTP(S)配置，构造文件服务时拒绝非法值，不从Host、X-Forwarded或请求参数取地址。真实HTTP测试在伪造Host条件下仍返回配置的完整公开前缀；示例域名仅断言输出，未宣称DNS/网关或多端已经可访问。平台磁盘根及默认数据库连接保持原值。
