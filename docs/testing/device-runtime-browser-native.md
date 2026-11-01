# 原浏览器真实双项目批量运行时验收

2026-11-01，C88；起点c86618a。当前已实现完整编排、原Web构建和离线护栏，**尚未执行真实浏览器业务或新MySQL租约**。C87的真实后端／CLI与本项原页面分别记录，不拼称UI已经通过。

## 完整场景

复用C87实际Production平台初始化、生产仓储元数据、两个旧完整工程、真实Web／OAuth／原设备绑定及同一文件字节审计。新增独立浏览器用途device-runtime-browser-native-v1与明确Fact；旧原生HTTP用途不能启用本项，当前源码、生产宿主／CLI、旧源及原Web完整产物均须先固定，再取得精确新范围授权。

浏览器只使用HBuilderX实际构建的原499前端源码产物，不另造登录／列表／按钮或业务接口；ego lite经原登录、工作区／设备与原菜单进入原项目列表，操作同一个批量按钮及确认。

| 阶段 | 页面要求 | 独立后端及本机核验 |
| --- | --- | --- |
| 部分失败 | 两个应用项目、未绑定项目和禁用项目逐项显示，不能报整体成功 | 原POST确实入队，两个真实CLI原v2计划应用；未绑定failed、禁用skipped |
| 完整成功 | 本轮移除未绑定配置后，再点原按钮，等待真实applied和原逐项成功汇总 | 新批次／任务，原配置、grant、短ticket及字节审计继续，不以排队当成功 |
| 回执未知 | 实际原POST先转发到真实后端并建立批次，再丢失运输回执、临时阻断status运输 | 不合成业务成功；记录原UUID，允许浏览器运输层重试同UUID，不要求HTTP恰好一次 |
| 刷新恢复 | 未知提示后刷新、保持原账号／项目／设备，再点原按钮只查原批次 | 后续新增原同步POST必须为0，status指向原batch；原任务实际完成 |
| 收尾 | 原UI退出；只清理本轮唯一origin的cookie／存储，关闭本次agent任务空间 | 两CLI注销、宿主与门面停止；同租约库／账号／凭据清理及独立核零 |

每个页面断言由实际ego操作及截图回执佐证；C#另核验原批次、真实状态与目标文件，不能仅凭人为标记认定业务通过。浏览器真实登录响应仅在本轮私有GUID凭据目录存会话，供后端独立只读查状态；不创建／注入身份，不把令牌、密码或sysParams写进日志或静态资源。门面正常响应逐字转发，只在明确unknown阶段注入运输故障；没有合成业务数据。

## 已执行的准备

- HBuilderX工程、Vue3、AppID__UNI__CF80CAE；实际CLI版本5.34.2026102909。技能脚本只支持macOS，Windows使用仓库既有D:/HBuilderX/cli.exe，先实际version和publish web --help，再执行：`D:\HBuilderX\cli.exe publish web --project E:\git\metarill\frontend\metarill_uni_admin --webTitle Metarill`。日志显示编译成功／导出Web成功，固定156文件、4907950字节；499前端源码前后摘要保持，没有上传／部署或运行Vite。
- 19项严格C#护栏／wire通过、0失败／0跳过：含原16项及新3项浏览器用途／原Web资产与源码漂移检查。新增3项与旧16项不再重复累计。
- 原预览及新门面共3项Node运输合同通过、0跳过，已纳入CI。测试后端／静态字节明确合成，证明响应及原头／正文保真、同UUID观测、未知回执丢失／503恢复、私有会话不进trace；不计原UI／MySQL／OAuth验收。
- 原776份产物／手写／前端／模板／元数据逐字保持。当前没有新建实库、账号、正式CLI profile、助手任务或Ego任务空间。

首轮编译因抽取共享文件审计时保留await却声明void失败，已修正为共用async Task并显式await，原日志保留。Node首轮测试误把中文直接放HTTP头，被ByteString拒绝；编码后又发现Node fetch忽略调用者Host覆盖，严格门面正确403；合成客户端改为真实HTTP发送同域Host并保持产品门禁，最终3项通过。未通过修改原页面、弱化Host或合成业务R200让测试通过。

## 来源与执行边界

H5输入／产物证明及日志位于.local/device-runtime-browser-20261101：frontend-source-before.json、frontend-build-receipt.json、h5-build.log、preparation及retry／closed日志/TRX、gateway-tests首轮／retry／final及gateway-final.log。完整当前来源／65宿主／23 CLI等数量在新固定准备完成后记录，不能借C87已结束的两库租约执行。

有持续进程时由本轮单一执行器保留PID、日志和停止句柄，未知只观察同一进程，不因为轮询超时重启。正式运行前保留精确Server-200新库／账号／临时凭据范围，逐Docker先验证远端身份；完成后按同轮所有权清理并独立核零。只清理本轮浏览器origin，不清用户整个profile。原完整原生HTTP模式继续独立运行，浏览器编排不改变产品、原界面或其它数据库的发布状态。


## 2026-11-01 C88 固定来源与精确新范围

6f85c8d28f5e61c563562af885427def02e9a045独立固定2314源码、65宿主文件、23 CLI输出、152正式输出；旧1e57bb9共2297文件完整固定，严格构建零警告错误，固定19项通过／0跳过。正式程序集自身纯文件Verify同时验证真实宿主／CLI／旧源和原Web证明；没有构造批准、连接数据库或启动Ego空间。外层4种失效租约零子进程，实际浏览器FQN以pending／ended／旧管理用途3次预期拒绝，不计正式通过。

首次把工作区H5输入与Git固定检出逐字对照时发现两份历史CRLF差异（pages/main/index.vue及一个测试源），未放宽摘要门槛或改原文件；保留失败和原工作区产物，将现有无链接依赖复制到固定检出，用同一HBuilderX5.34及AppID重新实际发行。最终固定499输入／156 Web文件／4907948字节，2314固定源码再次逐字保持，原776文件亦保持。日志保留uniCloud本地调试workspaceFolder告警，但本项不使用uniCloud服务，随后实际编译／导出Web成功，不隐去该告警。

新的精确范围：metarill_fresh_2ab4b0e2847d0414_platform、metarill_fresh_2ab4b0e2847d0414_business及mtf_2ab4b0e2847d0414@%。Server-200 MySQL8.4.11只读确认两库／账号／所有权不存在。计划在.local/device-runtime-browser-20261101/permission-plan.md，pending／not-started；同轮唯一浏览器origin为metarill-runtime-2ab4b0e2847d0414.localhost加专属端口，只清本origin及本次agent空间。未执行真实浏览器业务，新租约不得复用C87已结束授权。

新增固定证据：fixed-preparation.trx、fixed-build-state.json、browser-prepared-receipt.json、frontend-fixed-build-receipt.json、h5-fixed-build.log、frontend-line-endings.json、compiled-proof-verification.log、pending-guards.json与readonly-preflight.json。源码及原工作区产物分别保留，不将原工作区差异覆盖进固定源。
