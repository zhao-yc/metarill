# 设备关联回执的配置快照一致性

2026-11-02，C98；起点0dc86f8。本项修复在线本机助手的配置变更恢复，保持原页面、按钮、接口路径、请求正文、授权和持久意图格式。

## 问题与结果

助手恢复pending意图时，分别查询当前配置和当前关联。若第一次查询之后，配置及同binding编号的关联被明确更新，原客户端只比较第一次查询的摘要及第二次响应的matchesCurrentConfiguration，会把新配置关联记为旧请求成功。显式登记中复用现有关联的快捷分支也有同一问题。两次读取各自有效，仍不能拼接成同一个快照的证据。

新增三个测试先复现：原设备状态机14通过／3失败；恢复和首次登记都错误接受了跨配置回执，旧格式回执也被直接当成只读成功证明。原始红灯保存在忽略目录.local/device-association-snapshot-20261102/red.log。

## 修复

- 服务端的关联回执新增可选deviceSha256和configurationSha256，二者均从正在返回的同一条持久关联Scope取得。旧关联失效时仍返回旧快照摘要和原ETag，不能混入本次预检的新摘要；持久文件及完整性计算不变。
- 客户端只读确认必须同时匹配原意图的设备摘要、配置摘要、binding编号、项目／设备以及当前配置标志；同一个binding编号不足以证明成功。首次登记发现两次读取跨了配置，明确拒绝，保持原本地意图。
- 新格式摘要必须成对且合法。正常登记的直接响应若带新摘要，也必须与原请求一致；失败后仍只用原请求的快照回读，不采纳新配置或更换旧CAS。
- 旧服务端两项字段均缺失时保持兼容：不凭缺少快照的只读响应确认，改由原请求和原CAS交给服务端验证；旧服务端已有相同关联的POST仍幂等。不接受只缺一项、null或错误类型的半份新回执。

没有增加认证豁免，不修改grant、管理员、设备拥有权、目录摘要、运行时分端范围或明确替换规则。新增响应字段是已有预检摘要，不返回路径、令牌或服务端状态文件位置。原请求字段和原R结构保持。

## 实际验证

| 层次 | 结果与范围 |
| --- | --- |
| 设备状态机 | 19通过、0失败、0跳过；含跨两次读取的设备／配置变更、首次登记、旧服务端兼容、畸形／错配新回执及原恢复／身份检查 |
| 相关Node合同 | 96通过、0失败、0跳过，包含上述19项；另含运行时来源、路径指纹，不能与19相加 |
| C#队列与边界 | 严格Release／warnaserror，90通过、0失败、0跳过；持久旧／新快照、真实JSON命名、幂等字节、认证边界及运行时来源。补充JSON断言后又跑相同90项，不重复累计 |
| 本机CLI与回环HTTP | 3通过、0失败、0跳过；实际C#CLI能力／工程归属、一次关联、未知后保留原请求、明确旧摘要换绑；HTTP身份为合约替身 |
| 分端运行时回归 | 8通过、0失败、0跳过；实际C#CLI在普通／管理独占包各完成三种分端应用，另含回执恢复及意图不匹配拒绝。所选端实际文件恢复、未选端／归属与原意图保持 |
| 原文件保护 | 723个已跟踪前端／元数据／生成文件逐字一致，含原手写partial；本轮没有再生成或改页面 |
| 待批准材料 | A30、B123各自check-only通过，frozenVerified=true，仍pending／not-started、子进程0、databaseWrites=false；不消费任何新库授权 |

核心复核命令：

```powershell
node --test tools/metarill-agent-cli/test/generation-device.test.mjs tools/metarill-agent-cli/test/generation-runtime-scope.test.mjs tools/metarill-agent-cli/test/device-project-path.test.mjs
npm run check --prefix tools/metarill-agent-cli
$env:METARILL_DOTNET_PATH='E:\git\metarill\.tools\dotnet\dotnet.exe'
node --test --test-name-pattern '真实CLI设备|真实CLI明确回读旧摘要' tools/metarill-agent-cli/test/generation-helper.test.mjs
node --test --test-name-pattern '真实CLI v2' tools/metarill-agent-cli/test/generation-helper.test.mjs
.tools/dotnet/dotnet.exe test backend/tests/Metarill.Api.Tests -c Release --nologo --no-restore --disable-build-servers --verbosity quiet -warnaserror --filter 'FullyQualifiedName~GenerationAssistantDeviceQueueTests|FullyQualifiedName~GenerationAssistantDeviceBoundaryTests|FullyQualifiedName~GenerationAssistantRuntimeOriginTests|FullyQualifiedName~AgentDeviceControlGateTests'
```

日志和TRX保存在上述忽略目录，不提交配置或凭据。本项是受控并发时序、真实磁盘队列与本机CLI验证；没有真实MySQL／OAuth配置并发，也不转记为原浏览器、跨端或性能通过。C65其余正式矩阵和MySQL首版总验收继续。

## 跟进与回退

成套更新平台API及Node助手，可沿用既有核心绑定和持久意图，不需DDL、元数据回填、原前端或生成模板变更。旧客户端继续忽略新增响应字段；新客户端连接旧服务端时只重放原意图并由原CAS确认，不假定新字段存在。回退使用相互匹配的可信源文件，不改旧关联摘要或伪造成功状态；代码推送不代表生产部署。
