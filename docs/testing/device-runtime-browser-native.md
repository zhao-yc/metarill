# 原浏览器真实双项目批量运行时验收

2026-11-01，C88；固定6f85c8d，正式原浏览器／MySQL／OAuth／双本机工程完整场景已通过：1通过／0失败／0跳过。原页面、样式与交互保持；三批业务、回执丢失后刷新只读恢复、退出及精确清理均有独立证据。下方准备与pending记录为执行前历史，最终结果见文末；不代表其它设备或MySQL首版全部通过。

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

## 2026-11-01 C88 原浏览器正式通过与清理

用户call_a93c0c0acebe4b55b827becb3f64f02f批准本轮精确两库／账号／凭据与唯一origin范围后，固定6f85c8d28f5e61c563562af885427def02e9a045的正式FQN单次执行：1通过／0失败／0跳过，primaryErrorType=null、cleanupErrors=[]。真实浏览器使用ego lite空间27和原HBuilderX产物，经原登录表单、工作区／设备、原菜单进入项目页，实际点击原批量按钮与一次确认；没有合成业务成功或更改产品权限、页面、样式、模板。

| 场景 | 实际结果 |
| --- | --- |
| 部分失败 | a369e768-6a40-4dc8-a987-83a245aacd0d；两个真实CLI任务applied，未绑定项目failed、禁用项目skipped，原页面显示逐项失败汇总 |
| 完整成功 | f4ca822c-9eaa-48d4-b2a3-b0b9012c30dc；移除本轮未绑定配置后由原按钮发起，两项目applied、禁用跳过，原页面显示成功汇总 |
| 回执未知 | 30a8a09f-71e2-41fb-af74-d305ebeaea99；原POST先真实入队，再注入运输回执丢失／状态503；原提示保留UUID，不把排队或网络未知当成功 |
| 刷新恢复 | 真实刷新后同账号／项目／设备及原UUID保持，再点原按钮只读恢复；同步POST保持3次，状态查询20→21，新增同步POST为0；原两个任务完成并显示成功汇总 |
| 目标文件 | 三批共6个真实CLI任务均reported/applied；首批每工程写2文件（1份运行时源码及归属清单），后两批每任务0写入、删除均0。目标运行时精确匹配当前源码，第二工程前端及两份手写文件摘要保持 |
| 退出 | 原账号菜单→退出→原确认后回原登录表单，标题“登录 - Metarill”；旧浏览器会话真实接口返回HTTP200／R500“登录超时” |
| 浏览器清理 | 仅清理http://metarill-runtime-2ab4b0e2847d0414.localhost:62429的存储，localStorage／sessionStorage均0；空间27通过finish关闭一次，未清其它网站／profile |
| 远端及凭据 | 两CLI真实注销；本轮两库／账号／所有权删除后独立连接及新SSH只读核零。GUID密码／CLI／浏览器会话与外层临时连接／所有权凭据删除，租约ended |
| 文件保护 | 2314固定来源／65宿主／23 CLI／152输出、旧源2297与原Web156文件再次严格Verify通过；原776产物／手写／前端／模板／元数据字节保持 |

退出后曾按hash包含pages/login等待而超时，但原退出实际返回/#/；保留round19观察失败，没有重做退出或重放租约。后续以原登录表单、标题及旧会话拒绝独立核验，确认成功后才清理origin。该问题属于观察条件，产品界面未修改。

证据保留于.local/device-runtime-browser-20261101：2ab4b0e2847d0414下native.trx、test-result／verified／browser-verified／cleanup／independent-absence／credentials-cleanup；formal-closure.json、readonly-final-zero.json、delivery-protected-after.json；正式evidence内五个UI回执及01—07截图、restore-request-audit.json和logout-auth-audit.json。两完整工程、六任务计划／文件回执保留；原执行前计划单独备份，已结束租约不能重放。

本轮验证浏览器刷新恢复；verified.json的restartOriginalBatch=false如实保留，服务端同地址重启由C87独立证明。本轮未验配置变化CAS／换绑／全部撤权矩阵、Android／微信／iOS／物理硬件、并发或同条件性能，C65和MySQL首版其余范围继续保持未完成。
