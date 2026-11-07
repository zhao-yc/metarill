# 当前同模型C#基线与Linux原协议核验（V32）

日期：2026-11-02。更新同条件验收的C#候选产物至当前63f60dc，原Java登录镜像329f43e及Redisff02b58保持；本批是构建／禁网HTTP／工具检查，**没有运行正式双库初始化、有效密码登录或性能负载**。

## 完整模型及产物

63f60dc1fe36a78be4b38de0e400d73f457ae601独立Git归档2406来源，正规CLI生成667文件FullApplication，仍为原web-database-v1。沿用原3表46列6索引、3页3按钮1子页及6份公开手写业务。两轮正规再生成34文件字节稳定，6手写保持；严格CLI／Host构建零警告错误。包内完整项目元数据与V28基线逐字相同，按真实MetadataCatalog全部类型语义相等；原登录及首页HTML／style相同，未重新设计页面。

用当前固定Data真实方言对实际包内元数据离线编译，逐列类型／可空／主键／审计默认和6索引与原Java正式DDL比较，差异0；不以表数相同代替同模型，也不改成较易通过的关系或表。本轮没有实际查询数据库结构、SQL或行数据。

保留一个准备失败：构建已成功，但后置核对误写不存在的pages/index.vue。依据原pages.json确认真实首页pages/main/index后只补只读核对，没有重跑已完成构建；原异常记录保留。它不是前端恢复或产品缺陷。

## Linux运行载荷与独占镜像

构建输出实际71文件，其中包括项目引用的独立deps；运行载荷改用SDK标准publish，明确PublishIISAssets=false（已读当前SDK官方targets确认该过滤行为），实际64文件。原71构建目录保留，不手删文件凑旧计数；Linux无IIS，原页面／业务／认证内容保持。完整64文件均通过已有运行文件路径白名单，含Linux x64 Skia依赖。

Server-200每条Docker前核hostname=Server-200及192.168.31.200，显式unix socket。使用已核内容ID的原49649d4缓存tag，17份当前公开产物共3170512字节增量、仅删除**新镜像层**中的旧web.config，network=none／pull=false构建唯一新镜像：

- 名称：metarill-paired-current-548b4e9bac414556d66ff8877c59cd42:csharp-v1。
- 内容ID：sha256:ee05d99dbb52de8a51f7d42fcbb7a67d1b588c8bb0b637f937a8cba236975200。
- Linux amd64、来源标签63f60dc、唯一构建run与64文件清单绑定；构建前后原缓存tag仍49649d4，原镜像不改。

镜像及唯一/tmp/metarill-paired-current-image-548b4e9bac414556d66ff8877c59cd42上下文保留。没有拉浮动镜像、数据库／账号／Redis操作或生产部署。

## 真实HTTP及工具结果

两个独占、非root、只读根、禁网／不发布端口、0.5CPU／512MiB／128PID短容器分别启动实际Production/Web宿主；数据库配置为空。先核镜像/app全部实际文件集合**恰等于**64清单，再逐项SHA，额外旧资产也拒绝。

第一组21项通过：公开Cap精确GET／旧头、保护方法和相邻路径401、原两字段／三字段form及JSON认证拒绝、原第11笔429限流。第二组9项通过：重复字段、未知身份字段、大小写、缺字段及JSON类型边界400。没有植入会话、开发身份或放宽认证，也未试图用错误账号登录成功。两容器finally按完整ID／归属清理，另新SSH独立确认均不存在。

资源白名单及公开pending示例只更新C#为新ee05d99，Java／Redis不变。新增旧**合法内容ID**49649d4在文件／秘密／外部操作前拒绝的反例。Windows工具120项为116过／4POSIX跳过／0失败，命令显式PYTHONUTF8=0；94份当前公开tools来源逐字传到Server-200独占目录，Linux同120项全部通过／0跳过。Docker／MySQL均为回归桩，不算正式初始化或业务。

旧V31修订版2原pending／not-started范围及其原39材料保留快照，当前已superseded。实际旧入口退出2、真实口令／外部调用／三个启动标记均0；旧问题答复不能执行新版。尚未批准或执行新的双库链，需固定新工具与本镜像、准备当前材料并取得具体范围直接授权。

证据在.local/paired-current-runtime-20261102，包括完整来源／包／输出摘要、元数据语义与物理比较、镜像记录、两个原HTTP回执及独立核零、Windows／Linux日志及delivery-receipt.json。723原保护文件逐字不变；没有产品运行时修改、DDL／回填、合main或生产部署。

## 后续验收边界

V32只关闭当前C#候选及禁网原HTTP子集。V26双库显式初始化、原有效密码／三普通身份权限、原三动作及关系／文件／Excel矩阵、实际SQL／参数／索引／事务与同数据资源负载仍须执行。V08、V10、V11及MySQL首版没有因此通过，不推进其它数据库。
