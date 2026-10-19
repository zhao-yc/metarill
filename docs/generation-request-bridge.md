# 生成请求与本机准备基础合同

日常操作保留原三个按钮和确认，使用 [在线本地助手](generation-assistant.md)。首页和三个原GET不再导出JSON；离线请求/CLI仅保留为同一协议的底层准备能力和明确选择的诊断备选，不能替代用户已选择的在线流程。

## 已发布请求

`GenerationMetadataPublisher`复用完整打包发布脱敏规则。`GenerationUpdateRequest`固定 `type=metarillUpdateRequest`、`schemaVersion=1`、单action、projectId、metadata SHA、完整发布metadata与request SHA，不带token、本机目录、命令或连接。

`LocalGenerationUpdater.ReadRequest/ValidateRequest`有界4MiB，拒绝未知字段、重复JSON键、跨项目、未知action、摘要漂移或未脱敏配置。摘要是漂移检测，不是签名；只接收开发者审阅或真实认证平台投递的内容。

## 固定本机准备

```powershell
metarill update target-identity --target 已有FullApplication
metarill update prepare --request 已审阅请求.json --target 已有工程 --candidate 新空候选 --source-root 可信Metarill源码 --plan 新计划.json
```

`target-identity`只读验证归属后返回project/namespace/provider，无路径或私有配置。prepare的target/source/candidate必须互不包含，请求和plan必须在它们之外且互不覆盖；逐层拒绝链接。namespace只读目标原三层归属，不能由请求或假默认值指定。平台BackendOnly和不支持控件明确拒绝，不静默降级。

prepare使用同一完整packager产生隔离候选，再调用原 `update preview`，不写原target、不执行SQL/DDL、安装、构建或启动服务。保留原单action，返回 `preview-ready` 或 `dependencies-required` 及原plan/requiredActions，后者退出码3。错误时保留已经产生的候选/计划并明确未应用，不伪造成功。

在线助手可在同快照下把用户分别确认的actions形成精确组后重新preview；它没有离线prepare的scope覆盖选项，也不能把单action改成all。实际应用仍走原确认planSHA的 `update apply`；中断只读核对原 `update apply-status`，确证本机原事务才用recover，unknown不能重跑apply。

## 验收边界

基础已有严格协议、路径/归属、脱敏、真实临时包prepare/preview/apply回归。在线助手补真实OAuth、原按钮handler、固定CLI应用和回执/恢复验证，具体最新批次见在线助手文档。不把CLI产物、Node传输合约替身或Vue方法测试当HBuilderX/浏览器/真机验收，不宣称生成前端已完整符合原YZP交互。
