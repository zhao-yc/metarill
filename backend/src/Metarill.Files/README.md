# 文件库与对象存储

- 通过 `FileLibraryService` 提供上传计划、流上传、完成、地址解析、公开/受保护读取、删除以及基础目录；所有对象键服务端随机生成，调用者不能指定磁盘路径或远端存储地址。
- `FileLibraryOptions.RootDirectory` 默认 `.local/files`。默认本地文件需要持久化目录；S3中转已经接入固定私有配置与对象层，真实云端/供应商矩阵仍待验收，见下节。
- HTTP 路由实际位于 `/api/admin/sys/file-library`；返回给保留的 uni-app 控件的相对 URL 从 `/admin/sys/...` 开始，由已有 `environment.base_url` 统一添加 `/api`。读取模式沿用 `LOCAL_STREAM/REDIRECT_URL`，私有流由控件携带登录头下载。
- HTTP 内容端点用 `FileContentHandle.Stream` 返回可寻址文件流，开启 Range，并设置 `X-Content-Type-Options: nosniff`。内容类型使用服务端扩展名映射；不能信任上传者填写的 MIME。
- 计划由同项目合法文件控件的 `ControlDetail` 决定访问模式、大小、扩展名及数量。无列上下文默认受保护。公开状态只能显式配置 `PUBLIC`；记录必须完成大小/摘要核验并处于 ready 后才可公开。
- 受保护草稿仅当前超级身份/管理员或上传者读取；就绪文件保留超级身份路径，普通账号按真实业务引用与当前来源页/父范围验证，所有权不是永久授权。客户端pageId/recordId不授予权限；未证明的复杂组合继续拒绝。
- `FileReferenceMutationObserver` 必须随生成 Service 的业务事务执行。它核对实际页、字段、项目、物理记录及真实字段内容；只处理 `FileLibrary` 列，省略保持、显式 null 清空。引用与文件删除使用相同文件行锁；已被引用文件不能删除。
- 未提供完整父记录证明的复杂 `WhereSql` 范围明确拒绝；首期只支持空范围或真实项目范围。平台库和业务库不能跨库同步引用，各项目文件表必须与业务记录处于同一数据库事务。
- API 注册需要 `FileLibraryService`、`FileLibraryOptions`、`FileReferenceMutationObserver`；服务可按请求注入认证上下文。物理删除失败时数据库已经失效，返回 `StorageRemoved=false`，需后续维护清理，不能声称存储完全删除。
- 真正的隔离数据库测试使用 `METARILL_TEST_SETTINGS` 与 `METARILL_PLATFORM_METADATA`。文件基础设施必须已显式初始化；测试只创建随机 `mt_test_files_` 业务表、唯一ID文件记录及项目临时目录，最后清理，不改生产库。

- 文件服务、引用观察器及 HTTP Controller 位于独立 `Metarill.Files` 工程，平台与生成应用复用同一实现。命名空间沿用既有名称以保持调用兼容；基础设施行存储位于 `Metarill.Data`，不依赖平台注册器。
- 宿主显式注册 `FileMetadataContext` 的发布快照、同一个 `DatabaseSession` 与 `SqlRowStore`，并添加文件 Controller 所在程序集。启动不隐式建表。
- 目录移动按固定 ID 顺序锁定当前项目有效目录，拒绝父链成环；不依赖平台的项目表。HTTP 上传限制对应服务端最大 100 MiB，超出配置明确拒绝。

## S3兼容对象存储

默认仍为本地。需要S3时，在私有配置的Files.Storage中指定DefaultProfile及S3Profiles；平台对应Metarill.Files.Storage。连接、桶、前缀和凭据只来自部署配置，普通启动不连接云端或创建桶。请只登记已经配置完整的条目；空的S3Profiles不会启用任何云端供应商。

```json
{
  "Files": {
    "Storage": {
      "DefaultProfile": "assets",
      "S3Profiles": {
        "assets": {
          "Endpoint": "https://s3.example.invalid",
          "Region": "us-east-1",
          "Bucket": "your-private-bucket",
          "Prefix": "metarill",
          "AccessKey": "<私有凭据>",
          "SecretKey": "<私有凭据>",
          "ForcePathStyle": true,
          "AllowHttp": false
        }
      }
    }
  }
}
```

原控件和服务器中转上传协议保持，旧本地文件不随默认配置改到云端。S3对象定位包含固定位置摘要；移动桶、端点、前缀、区域或寻址方式时保留旧别名并新增别名，不能把旧对象解释到新位置；同位置可轮换凭据。供应商需实际证明条件写与内容校验，SDK接入不代表所有S3兼容品牌已验收。

云端读取会有界下载并核对实际摘要，再返回原可寻址只读内容流；临时副本在流关闭时删除。公开/受保护地址、Range/HEAD及真实项目/页面/引用权限保持，不返回云端凭据或允许客户端指定桶/URL。该方案有网络和临时磁盘开销，跨端、真实MySQL/云端事务与同条件性能须分别验收。云端直传、OSS及完整供应商矩阵仍待，不能将本节视为这些能力已完成。

已有工程通过原preview/apply流程成套同步文件运行时、宿主和精确NuGet依赖，保留手写partial、AppID与私有配置。关闭默认S3只影响新上传；已存S3对象仍需原配置与新运行时，不能直接回退到仅本地版本。
