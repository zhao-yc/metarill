# 设备关联持久请求恢复

2026-11-01，C90，起点f0e94ae；仅修在线本机助手的持久pending恢复，原页面、样式、交互、C#接口、模板与生成产物保持。

## 问题与修复

设备关联POST可能已在服务端登记，但写入回执及同一次失败后的回读都不可用。本机此时保留原pending意图。原实现下一次轮询直接重发POST，只在重发又失败后才回读；新增实际磁盘意图／受控RPC测试首先复现POST从1次变2次（该次12项中11通过、1失败）。

恢复现先用原deviceId查询设备配置及关联。只有设备与配置摘要仍等于持久原请求、关联属于当前项目／设备／bindingId且matchesCurrentConfiguration为true，才在再次核对认证身份后记录bound，不新增绑定POST。无法回读确认时，仍仅提交原请求正文及旧CAS，不重新预检采纳新配置、不重建bindingId、不默认换绑。首次POST异常后的回读沿用同一核验函数。applied:false保持原义：没有应用工程源码。

未更改私有意图schema、完整性摘要、路径守卫、实际CLI能力／工程归属检查、服务端认证或CAS规则。不同设备意图仍拒绝；服务端同内容幂等语义保持。该修复不新增或绕过能力目录豁免。

## 验证与边界

定向实际命令：`node --test tools/metarill-agent-cli/test/generation-device.test.mjs`，最终14通过、0失败、0跳过。新增四项覆盖两次回执均不可用后的持久恢复只读确认、配置已变的原请求保留、失效／其它关联与设备摘要变化拒绝误报，以及回读后认证身份变化不落盘成功。原意图正文与旧CAS、一次POST计数和实际磁盘状态均有断言。

语法检查：`npm run check --prefix tools/metarill-agent-cli`通过。完整CLI回归首次未设置SDK路径，114通过／22失败／0跳过，22项真实C#CLI用例均在before钩子找不到dotnet，未当成通过。指定仓库实际SDK路径METARILL_DOTNET_PATH后，原命令`npm test --prefix tools/metarill-agent-cli`重新执行，最终136通过／0失败／0跳过，退出码0，耗时约10分42秒；定向14项已包含其中，不相加。实际Node24.17.0／.NET SDK10.0.402。六个普通／管理应用分端组合、真实CLI回执恢复与意图不匹配拒绝均执行通过。

以上设备状态机用受控RPC和实际磁盘意图，完整CLI中的HTTP身份／登记也仍是回环替身；不计为新增真实MySQL／OAuth、并发配置CAS／换绑或其它端正式验收。C87／C88的既有固定双项目及原浏览器通过范围保持，C65其余矩阵继续待验。B119独立冻结17d38cc与新精确32表保持pending，未使用B118已结束授权重跑。

私有日志保存在.local/device-pending-recovery-20261101：red.log、green-first.log、green-final.log、check.log、full-cli.log、full-cli-with-sdk.log。日志、临时意图、环境参数不提交；受保护776文件在完整回归结束后逐字核对，0变化；final-result.json保存最终结果。B119原check-only同时确认来源／输出保持、childCalls=0、databaseWrites=false，租约仍pending／not-started。

## 跟进方式

仅需将Node助手lib与配套CLI从可信源码按既有流程更新。固定CLI或受管来源字节变化仍需按原机制建立对应绑定，不手改旧摘要绕过；无需DDL、元数据回填或原前端再生成。没有生产部署。
