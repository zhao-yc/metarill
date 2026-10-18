# 本地文件库

- 通过 `FileLibraryService` 提供上传计划、流上传、完成、地址解析、公开/受保护读取、删除以及基础目录；所有对象键服务端随机生成，调用者不能指定磁盘路径或远端存储地址。
- `FileLibraryOptions.RootDirectory` 默认 `.local/files`。正式部署必须给该目录提供持久化存储；当前只实现本地存储，没有假装完成 S3 适配。
- HTTP 路由实际位于 `/api/admin/sys/file-library`；返回给保留的 uni-app 控件的相对 URL 从 `/admin/sys/...` 开始，由已有 `environment.base_url` 统一添加 `/api`。读取模式沿用 `LOCAL_STREAM/REDIRECT_URL`，私有流由控件携带登录头下载。
- HTTP 内容端点用 `FileContentHandle.Stream` 返回可寻址文件流，开启 Range，并设置 `X-Content-Type-Options: nosniff`。内容类型使用服务端扩展名映射；不能信任上传者填写的 MIME。
- 计划由同项目合法文件控件的 `ControlDetail` 决定访问模式、大小、扩展名及数量。无列上下文默认受保护。公开状态只能显式配置 `PUBLIC`；记录必须完成大小/摘要核验并处于 ready 后才可公开。
- 首期受保护内容只允许同项目所有者或管理员读取。提供 pageId/recordId 不会自动授予访问；复杂页权限共享读取尚未复刻，明确拒绝其他用户。
- `FileReferenceMutationObserver` 必须随生成 Service 的业务事务执行。它核对实际页、字段、项目、物理记录及真实字段内容；只处理 `FileLibrary` 列，省略保持、显式 null 清空。引用与文件删除使用相同文件行锁；已被引用文件不能删除。
- 未提供完整父记录证明的复杂 `WhereSql` 范围明确拒绝；首期只支持空范围或真实项目范围。平台库和业务库不能跨库同步引用，各项目文件表必须与业务记录处于同一数据库事务。
- API 注册需要 `FileLibraryService`、`FileLibraryOptions`、`FileReferenceMutationObserver`；服务可按请求注入认证上下文。物理删除失败时数据库已经失效，返回 `StorageRemoved=false`，需后续维护清理，不能声称存储完全删除。
- 真正的隔离数据库测试使用 `METARILL_TEST_SETTINGS` 与 `METARILL_PLATFORM_METADATA`。文件基础设施必须已显式初始化；测试只创建随机 `mt_test_files_` 业务表、唯一ID文件记录及项目临时目录，最后清理，不改生产库。
