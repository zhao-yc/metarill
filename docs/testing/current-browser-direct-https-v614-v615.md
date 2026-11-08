# V614 与 V615 当前候选的浏览器直连 HTTPS

2026-11-08，固定产品 `5c8fcb823491e2e25029ffa40cb2cfdc851c0abb`。平台和独立 Full 分别从同提交的固定源码和 V612 生成包严格发布 Windows 宿主，各 51／51 个发布文件。独立 Full 的八份 Windows 锁与 Linux 锁在通用目标上的依赖逐项一致。验收数据库为 Server-200 自有隔离测试库。

平台 H5 的 500 份源码与上一已验归档一致，实际使用其 156 份 H5 产物。独立 Full 的 393 份前端源码中，验收副本只配置 Web AppID 和当前同源 API 地址。HBuilderX 5.34.2026102909 导出 60 份本地 H5 文件。五轮逐份核对同一套宿主、H5 与元数据。

## 浏览器证书验证

V614 使用短期自签叶证书和三个独立 Chrome 配置。两个指定 `.localhost` 名称均获得浏览器的 `secure` 和 TLS 1.3。错误主机名被拒绝，空白配置和另一个新配置均以证书颁发者无效拒绝。叶证书仅预置在验收配置的证书库，未启用忽略证书错误。两套实际 H5 回环网关也使用 TLS 1.3，独立客户端校验正确主机名及首页摘要，并验证错误主机名和未信任证书均失败。[Chromium 配置证书库](https://chromium.googlesource.com/chromium/src/+/main/chrome/browser/net/server_certificate_database_service_factory.cc)

## 原页面和真实业务

| 场景 | 已验证 |
| --- | --- |
| 平台桌面 1280×840 | 原登录、项目选择、菜单、确认退出均以浏览器直连 HTTPS 完成。原 `/api` 请求实际返回业务成功。 |
| 独立 Full 移动 Web 390×844 | 普通编辑账号完成原登录、菜单、客户分页、新增、详情回读、修改、删除及退出。浏览器观测平台 7、独立应用 17 个同源 API 回执。新数据库连接核对客户备注更新、逻辑删除和两个会话注销。 |

前四轮分别在登录前等待、桌面操作菜单、移动保存点击和保存后遮罩消失前停止。每轮保留实际回执，复验仅调整浏览器定位、触摸事件或等待条件，产品源码及页面保持一致。五轮分别使用独有的三个测试库、临时账号和 Redis。每轮结束用新连接确认库、账号、所有权标记与 Redis 容器和配置目录已清理。本机验收浏览器配置、私钥工作副本和应用临时凭据已清理，四个系统证书存储中的本轮精确证书指纹均不存在。

本轮观测到 `04b747d2` 的 push 触发被取消且无作业，同 SHA 的 PR 触发仍在等待，先前 PR 的 15 个作业已有 11 个成功、4 个运行中。该现象符合现有并发组默认替换等待项的规则，GitHub API 未给出逐条取消原因。最终发布提交仍须核对 CI。[push 运行](https://github.com/zhao-yc/metarill/actions/runs/42435826969) · [PR 运行](https://github.com/zhao-yc/metarill/actions/runs/42435830859) · [GitHub 并发组规则](https://docs.github.com/en/actions/concepts/workflows-and-actions/concurrency)

实际部署环境、其余首版角色与业务组合、真实 Tiny Cloud、同条件性能及最终 CI 继续按 [首版签收清单](../mysql-first-release-milestone.md) 核对。小程序和 App 单列为后续兼容性验收。
