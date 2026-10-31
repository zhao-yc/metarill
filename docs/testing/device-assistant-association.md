# 原设备与本机助手关联准备

日期：2026-10-31；C65，起点49c6e84。原批量按钮逐项目在线助手方案已经确认。本切片完成配置读取、关联、CLI准备及受控验证；**真实Agent通道仍被现有中间件拦截，批量按钮仍404，不能计为已接通。**

## 已实现的组件

- C#只从实际SqlRowStore读取当前账号拥有的活动设备，再按设备/真实项目读取原最新配置（更新时间、ID倒序）。配置账号不一致时不回退旧行；不以客户端设备选择、项目或hash作认证。
- 保留is_java/java_path/is_uniapp/uniapp_path。仅启用端目录参与匹配，固定服务端表名/投影/参数；无路径或坏开关不能建立关联。锁定驱动MySqlConnector2.6.1本地XML说明tinyint(1)可读为bool，新增CLR bool兼容，不新增品牌分支；没有实库持久化结论。
- C#/Node共用规范路径向量与版本化SHA。只匹配明确目录，Windows仅ASCII大小写/分隔符统一、POSIX大小写和Unicode名称保留；不解析服务器上的Windows路径，不展开相对目录或任意脚本。
- 关联独立存入设备状态，按账号/设备/项目分槽，不改原v1核心binding字节。要求真实核心绑定的账号/项目/grant、apply及允许sync-runtime、固定CLI分端协议2、当前设备/配置预检摘要和启用端路径摘要。换绑需确切旧ETag；同内容幂等，失效关联可回读旧ETag并标记MatchesCurrentConfiguration=false。
- 新增三个实际控制器入口Bind/Lookup/Configuration；逐请求RequireAgent、平台管理员、身份一致、BackendOnly/query拒绝及服务器实际拥有权检查。当前尚未获得中间件专用控制通道登记许可，因而真实Agent仍会先被能力目录拒绝；匿名HTTP到达认证拒绝不证明实际Agent可调用。
- CLI新增device-bind/device-status及首次bind可选device-id，旧核心BIND_KEYS不变。固定C#CLI实际提供只读update capabilities。配置预检先固定摘要，再落私有意图和单次请求；网络未知先回读，恢复仅重放原请求，不自动采纳新配置或换新bindingId。整个登记过程不生成/应用源码，输出applied=false。

## 实际验证与原失败

| 验证 | 结果与证据范围 |
| --- | --- |
| 元数据/实际仓储受控ADO | 最终严格518通过、25条件跳过，0失败；其中24个设备读取/路径专项，覆盖真实固定SQL、项目/拥有权/墓碑、最新行/坏账号不回退、配置/重置摘要、无路径/坏标识/布尔驱动值、共享路径向量。没有真实MySQL连接。 |
| API/真实队列与边界 | 最终严格120通过、49条件跳过，0失败；新增关联幂等/重启、核心v1字节保持、CAS、变更ETag、账号分槽、坏配置/路径/grant拒绝、真实控制器拒绝及实际Program三个匿名/no-store入口。合成身份不代替OAuth。 |
| Node/CLI完整合约 | 93通过、0失败/跳过；包括50个目录测试、9个设备意图测试、原CLI与生成/事务恢复，以及新增3个实际固定C#CLI+本地HTTP设备用例。新用例核对原v1profile、目标工程逐字保持、固定配置恢复、明确旧摘要替换；HTTP身份/注册为合约替身，没有真实grant。 |
| 实际中间件漏接复现 | 独立Probe引用实际已编译Agent与新控制器MVC元数据，显式合成Agent；新Bind进入普通业务能力分支，未到控制器，非零退出。不是正式认证验收。 |
| 自动权限审核 | 拒绝将新三个动作登记到原专用通道，理由为跳过能力目录/业务账本及未获明确授权；AgentTaskMiddleware原文件未修改。只生成待审阅文本补丁，未应用、未改名挤进旧Bind、未通过其他工具绕过。 |

C65最初真实Agent漏接不是由上述替身HTTP发现，而是在检查完整实际服务链时复现；不能把前面的绿灯推成设备功能完成。待批准的精确类型/动作、保留认证和影响见[控制通道决定](../device-control-gate-decision.md)。普通能力目录的备选需要另建管理能力/权限/账本模型，未擅自替换既有架构。

私有证据：device-scope-first.log、device-queue-first.log、device-association-{api,metadata}-closed.log、device-association-node-final.log，以及device-control-gate-20261031/red.log和对应TRX。均在E:\git\metarill\.local，不提交日志/构建/私有意图。早期路径原型Windows标准输入未指定UTF8的失败仍保留；产品使用UTF8文件/HTTP JSON，原型修正后49跨语言向量已核对。

## 后续与升级边界

无需DDL、数据回填或原页面再生成；前端页面/样式/模板及backend/generated没有改动。API/CLI/Generation和CLI lib需配套从源码重编译/更新。固定CLI字节变化必须按原机制新建合适绑定，不编辑已有摘要绕过；普通已有v1profile不被静默加设备字段。

待明确批准后，才登记三个真实控制动作并补正向/未知方法/其他控制器的中间件验证。还须管理完整工程v2、真实MySQL/OAuth/CLI、批量计划与逐项目推进、v2任务来源复核、Node scoped计划应用及原按钮汇总/未知回读。当前Node仍按既有v1生成任务工作，新设备关联代码不是v2批量任务应用已经完成的证明。原批量接口尚未实现，S3/C53/B102/C38待批准范围保持独立，无远端资源操作。
