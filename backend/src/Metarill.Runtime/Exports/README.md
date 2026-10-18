# 私有页面导出

- `ExcelPageExportService` 实现 Core 的 `IPageExportService`。生成层先完成页权限、真实项目范围、筛选及导出列投影；本服务只接受投影结果，不执行客户端 SQL。
- 注册同一个单例供 `IPageExportService` 和下载端点使用，并注册 `ExportCleanupWorker`。下载 `/api/admin/sys/export/export?exportId=...` 必须先取得真实 `RuntimeActor`，再调用 `OpenAsync`；票据同时绑定账号和项目，管理员也不能借用他人票据。
- 默认私有根目录 `.local/exports`，票据15分钟失效。到期后立即拒绝新下载；分钟级后台任务负责删除，已合法打开的流可完成传输。不得把目录作为静态资源公开。
- 票据与文件路径分别随机生成，不向客户端返回物理路径。当前票据存在单进程内存，重启后失效；过期遗留文件按受限随机文件名规则清理。多实例部署需要会话亲和或后续共享票据存储，不能声称已经支持任意实例下载。
- 默认上限：50,000数据行、200列、500,000单元格、单格32,767字符、文本总量1,000万字符、单文件64MiB、1,000有效票据、2个并行生成。超限明确失败并清理临时文件，不能静默截断。
- bool、普通数值、日期和时间写为对应Excel类型；超过15位的大整数或高精度小数写为文本，避免Excel精度损坏。原本的字符串始终为文本，保留前导零、单引号，公式前缀不会成为公式或外链。带时区日期以ISO文本保留偏移量。
- 下载使用 `ExportContentHandle.Stream/FileName/ContentType/Length/ExpiresAt`；流可寻址，HTTP层可启用Range。测试会真实生成、读回并解包XLSX验证类型与公式节点，不依赖生产数据。

依赖已经核验：[ClosedXML 0.105.1官方包](https://www.nuget.org/packages/ClosedXML/0.105.1)，[对应版本MIT许可证](https://github.com/ClosedXML/ClosedXML/blob/0.105.1/LICENSE)。不要求服务器安装Excel。

## 读取前资源门禁

新生成的 Controller 使用 `CreateAsync(actor, prepare, cancellationToken)`：Excel 服务先取得并发名额、预留有效票据额度，才调用 `prepare(MaxRows, token)`。生成 Service 在数据库查询中设置 `LIMIT(MaxRows+1)`，超过上限直接拒绝，在关系补全和构建工作簿前终止，不返回截断文件。准备失败或取消时释放预留额度。

原 `CreateAsync(request, actor, token)` 继续通过同一内部实现创建文件，适用于调用方已经准备好投影的兼容场景；它不能约束调用前的读取成本。更新 Core、Runtime 和生成器后必须配套再生成 Controller/Service。自定义导出宿主需要实现新重载；只实现旧重载时默认拒绝且不执行准备委托，不能回退到无限读取。

本轮真实业务库验收用3条唯一测试记录和1行服务端上限：实际SQL为 `LIMIT 0,2`，超限无部分文件，精确1行成功；票据额度已满的后续请求没有执行额外SQL。测试记录已逻辑删除，测试宿主已停止。
