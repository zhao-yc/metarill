# 生成审计字段的原日历语义

日期：2026-10-29。修改前基线 `7e60e0b`。本批范围为生成 Service 的 insert_time、update_time 和软删 delete_no，不包含平台手写工作台、文件库内部日期或会话/OAuth期限。

## 生成源头

冻结原 Java 的真实 `MyMetaObjectHandler.insertFill/updateFill` 使用 LocalDateTime.now()；前批已在真实原 classpath 实际执行取证。旧 C# BackendRenderer 则在三个写入位置生成 DateTime.UtcNow。UTC+8 部署时，这会让同条件样板的原审计日历值相差约八小时，不能宣称双端写入数据一致。

Core 增加 `BusinessCalendar.Now`，返回宿主当前日历且 Kind=Unspecified，表达 LocalDateTime/MySQL DATETIME 的无时区值。BackendRenderer 的新增、更新和软删统一引用此合同；复制与导入继续复用生成 Service 的正式写入路径，不增加旁路。没有把数据库默认函数当应用时钟，也不依据字段名字对客户端提交时间做统一偏移。

手写 partial、原页面及其可编辑标志不变；普通更新继续保护省略的 insert_time，不改变审计身份或事务入口。真实 JSON 使用原 JsonDefaults，仍返回 yyyy-MM-dd HH:mm:ss，不附加 Z 或时区偏移。

## 完整包与既有项目

候选完整包必须携带 BusinessCalendar.cs。删除源码及清单也不能让依赖此合同的生成 Service 被判为可应用；普通/显式精度候选分别覆盖拒绝门禁。旧目标允许升级，不要求它预先包含新文件。

既有应用先预览、成套更新 Core 与生成器，再按原元数据重新生成受管后端 Service 并构建。原前端无需再设计或修改；手写 partial 不覆盖。元数据、DDL、回填均不是本批必要步骤。旧数据如已写入 UTC 日历，必须按来源单独核对，不自动整体平移或重写时间。

## 未完成边界

本批不能证明应用 MySqlConnector 的实际 DATETIME 读写、物理列舍入、数据库/容器时区或正式 HTTP 结果全部一致，也不是性能成绩。平台手写元数据工作台及文件库仍有既有 UTC 审计写入；文件库的上传过期判断与 insert_time 绑定，不能只改其审计值而让期限相差八小时。该部分需沿真实依赖另行处理，安全会话/OAuth/token 的 UTC 瞬时继续保留。

A18 的物理精度与完整导入保存、C29 正式账号窗口、V08/V14 同条件功能/SQL/数据/资源及性能门槛继续待验。回退必须同时回退生成器、受管产物和 Core 合同，不能删除仍被当前生成代码引用的文件。
## 当前验证

生成审计/缺源码/完整包首批10项零失败零跳过；审计断言改为直接取独立系统日历后，复用夹具的关系写入、原Vue分支及新审计行为16项通过，不能把两批重叠项相加。新测试执行实际生成Service的新增/更新/复制/导入/软删，核对交给真实Mapper的实体、更新不覆盖旧insert_time、Kind=Unspecified及原JSON格式；只有ORM响应是明确离线代理。

正式再生成后Security139/0跳过、Metadata351/25实库跳过、API69/47、Agent351/25、兼容18/11，均零失败。CLI严格构建零警告零错误。215份产物两轮稳定，仅14份生成Service及两份归属摘要变化，表达式差异精确核对；24份手写partial与492份原前端逐字保持。本轮未运行浏览器、设备、真实MySQL写入或性能负载。

证据目录 `.local/business-calendar-20261029` 保留首轮测试命名空间编译失败、修复后的TRX、生成前逐字副本与两轮摘要。源码与证据内不含数据库连接或凭据；前批Java时钟证据仍在 `.local/temporal-runtime-20261029`。最终旧目标专项1项通过、0跳过：从当前原样板明确还原旧UTC审计表达式并移除新合同时重算双层归属，旧目标实际严格构建，再实际预览/应用新增合同、复验更新零差异和升级后严格构建。保留原partial及Vue；这是明确的旧行为模拟，不声称是历史版本包逐字复刻。最终903份受核验来源在最后窗口内保持，回执为final-receipt.json。
