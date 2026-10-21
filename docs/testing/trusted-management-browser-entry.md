# 可信管理原页面联调与浏览器验收入口

此入口把同一工厂冻结目录、生成后端、实际Production Program和原uni-app前端装在同一次隔离测试中。它仅用于本项目已授权的Server-200隔离环境，不发布完整管理Full、不接原初始化按钮、不放宽Runtime A或产品CORS。原页面、样式和交互仍来自既有资源；普通测试默认不启动人工等待。

## 执行与停止

事先准备私有测试设置和实例pin，保持原严格主机、server_uuid、测试库及自有项目检查。下面只展示配置入口，不提交配置内容：

```powershell
$env:DOTNET_HOST_PATH = 'E:/git/metarill/.tools/dotnet/dotnet.exe'
$env:METARILL_TEST_SETTINGS = 'E:/git/metarill/.local/test.settings.json'
$env:METARILL_ROLE_READ_INSTANCE_PIN = '<已核验的私有instance-pin.json绝对路径>'
$env:METARILL_MANAGEMENT_BROWSER_LEASE_SECONDS = '1800'
$env:METARILL_MANAGEMENT_BROWSER_APPID = '<你已有的DCloud AppID>'
& $env:DOTNET_HOST_PATH test backend/tests/Metarill.Generation.Tests `
  --filter 'FullyQualifiedName~Generated_management_http_uses_persisted_web_identity'
```

时限必须60至1800秒，不配置即保持原回归。每轮创建新的随机项目、两个后台、普通编辑身份；只在这轮角色上为已有动作权限的三个管理页补菜单可见权。固定服务、页权、物理图和前端发布缺口仍保留。角色、账号、会话等全部在原六表所有权范围内清理，不创建表或执行SQL GRANT。

私有回执位于 `.local/trusted-security-runtime/mysql-<本轮编号>/browser/`：ready.json包含临时登录口令，只由验收进程读取，不输出或提交；preview.json仅包含回环地址、前端路径和时限，可用于预览工具。只有私有前端副本的manifest AppID和手写application.config.js被配置，产品模板和原工程不变。

先在私有前端目录安装锁定依赖并执行Node验证，再用HBuilderX导入并发行Web，明确关闭云托管。构建完成后：

```powershell
node tools/acceptance/serve-management-browser.mjs '<本轮preview.json绝对路径>'
```

该工具固定回环监听及上游目标，静态目录按真实路径限制；API原样转发状态、身份头、正文和响应。日志只记录方法、去掉查询的路径、状态和时间，不记录口令、令牌或正文。只用于构建产物联调，不替代浏览器渲染或真机验收。

结束时先停止命令行和配置路径均属于本轮的预览进程、关闭本轮HBuilderX工程，再在同目录用临时文件原子替换release.json：

```json
{"leaseId":"本轮ready.json中的精确值","status":"build-only"}
```

`completed`用于结束已执行的人工阶段，也不能单独当作UI通过证明；`build-only`明确不计浏览器。未知状态、旧轮次、重复键和额外字段均拒绝并令本轮明确失败。无论成功、超时或错误，finally移除临时口令文件，原测试释放Program并精确清理自有项目。正常释放后继续原管理HTTP断言；测试和数据库清理结果须另核，不能只看释放信号。

## 2026-10-22 实际结果

原前端由同一冻结元数据准备，27个锁定依赖从本机缓存安装，Node路由/组件闭包验证通过。HBuilderX 5.26 Vue3本地发行成功；实际命令为project open --path、publish web --project --webTitle Metarill --webHosting false，结束后project close --path。子命令project open --help不受支持，未将帮助错误当作发行结果。

同源HTTP验证通过：index、原工作台编译块、与原源码一致的头像字节可读；匿名菜单401，原表单登录和R.map令牌有效，普通身份看到本项目三个管理菜单及原工作台，账号页和工作台roles可读；原退出后同令牌401。原始口令仅临时保存在随后移除的ready.json，未进入结果回执或代理日志；令牌只在请求内存中使用。这是实际Program与构建产物的HTTP联通，没有执行页面交互。

原Ego空间24已不存在，当前空间列表无同名空间。按ego-browser技能的同目标单空间与恢复限制，已询问是否允许重建；尚未收到答复。本轮以build-only释放，未新建空间、接管其他空间或换浏览器绕过。因此原管理页面的实际渲染、表单和工作台交互仍未验收。

最终默认模式边界及真实管理/三并发15/15，人工构建阶段后原HTTP测试1/1，均零跳过；批次有重叠，不累计。私有代理2/2覆盖回环/到期限制、状态/身份头/正文保真、静态查询与越界、日志不含输入值。初次人工会话因辅助目录保留..触发路径保护而失败，规范化辅助根目录后成功；未放宽产品路径校验，失败原件保留。

包括失败批次在内的7轮自有项目，已用新连接核验六表42个范围为零；临时登录文件、私有前后端和临时HBuilderX工程均关闭。证据在.local/trusted-management-browser-20261022，不提交原始口令、日志、依赖或产物。浏览器、设备、性能、初始化和完整管理发布继续保留原验收边界。
