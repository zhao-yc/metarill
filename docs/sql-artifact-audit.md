# 私有 SQL 采集文件核验（V45）

日期：2026-11-02；起点72ee83b。只补诊断采集与文件读取合同，不修改产品接口、元数据、原页面、生成模板、业务权限或数据库。MySQL 首版仍待完整验收。

## 读取合同

`tools/performance/audit_sql_artifacts.py` 只读已有私有文件，不发送 HTTP、SSH、Docker 或 SQL。可信协调器须独立冻结预期文件及其 SHA256、采集目录的 index.json SHA256，不能读取实际结果后自动放宽预期。

预期格式为 `metarill-sql-artifact-expectations-v1`：明确 java／csharp、32位 runId、固定来源和每一个样本。每个样本须有唯一32位 sample、退出状态、原链成功标志、命令数、事务设置／边界数、命令／边界失败数。Java 来源是完整排序后的 DataSource Bean 名；C# 来源是实际 Data 程序集摘要和允许数据库名。它们只是本文件合同的固定声明，物理数据库及源码到运行包的来源仍须由协调器另证。

```json
{
  "format": "metarill-sql-artifact-expectations-v1",
  "side": "java",
  "runId": "1234567890abcdef1234567890abcdef",
  "source": {"dataSources": ["dataSource"]},
  "samples": [{
    "sample": "1234567890abcdef1234567890abcdef",
    "statusAtExit": 200, "chainSucceeded": true,
    "commands": 3, "transactions": 5,
    "failedCommands": 0, "failedTransactions": 0
  }]
}
```

此例只对应明确替身数据库的 Spring 测试，不是正式业务预期。后续真实运行的登录、业务和必要探针都须预先列入；读取器拒绝未标记／未计划请求、重复或遗漏样本。不能静默忽略健康检查来掩盖采集缺口，也不能把退出时 HTTP200 当作 R 成功。

读取器逐项重算闭合请求计数、文件摘要、scope命令／事务数量、序号、参数证明版本与摘要形状、连接／事务引用、API时间区间及失败结果。Java 只接受明确标准 JDBC 操作，按单线程序号校验 API 先后；C# 命令与事务分别编号，按连接校验事务先后。提交失败不是提交成功；只有随后实际成功回滚等明确结束才能闭合。未知事务、未结束事务、抹去活动事务引用、未调用驱动却自报成功都会拒绝。

计时只比较同一进程的单调时间，支持 Java nanoTime 负原点和有符号回绕，不跨进程比较绝对时间。事务区间按连接建立索引，避免每个命令扫描全部事务。读取器不翻译 SQL、不读取查询结果、不推断服务器隐式事务，也不把两端事务 API 数量相等作为语义等价。

文件采用严格 JSON，拒绝未知／遗漏字段、重复键、NaN、bool冒充计数、未知值格式和遗漏摘要。预算为4096个样本、单类65536条scope记录、单文件64MiB、索引／预期4MiB及合计512MiB；拒绝链接／reparse路径、缺少／额外／未完成临时文件。报告须在采集树外新建，不覆盖已有报告。硬链接和散列本身不证明产物来自真实应用，亦不能抵抗采集期间有权限的并发替换；须冻结关闭后的完整目录并由可信协调器保管摘要。

```powershell
# 仅示意参数；必须替换为本轮独立冻结的真实摘要和私有路径。
python tools/performance/audit_sql_artifacts.py `
  --directory <本轮已关闭采集目录> `
  --expectations <本轮固定预期.json> `
  --expectations-sha256 <预期文件SHA256> `
  --index-sha256 <独立核对的索引SHA256> `
  --output <采集树外全新报告.json>
```

成功只设置 `evidenceInternallyValid=true`；`semanticComparisonAccepted`、`physicalTargetVerified`、`performanceAccepted` 和 `releaseAccepted` 始终为 false。参数摘要的长度／格式可以检查，但没有原输入与本轮key不能重算每一个真实值，不能据此宣称原值正确。失败只输出固定说明，不回显SQL、参数、异常正文或私有路径。SQL本身可能包含字面量，采集文件与独立预期均不提交公开仓库。

## Java 纳秒输入证明 v2

原 Java 审计字段使用 `LocalDateTime.now()`，可能包含不对齐100ns的纳秒。不能为了通过配对截断原参数或改动原业务代码。采集快照改报 `metarill-sql-values-v2`，原16个共同v1向量的kind／编码／摘要保持不变，C#仍为v1。

| 新kind | 固定原生类型 | HMAC输入的值部分（UTF-8） |
| --- | --- | --- |
| java-datetime-nanos | java.time.LocalDateTime | 以1970-01-01为0的epochDay、竖线、当日纳秒数；仍限公历1—9999年 |
| java-time-nanos | java.time.LocalTime | 当日纳秒数十进制 |
| java-duration-nanos | java.time.Duration | 有符号总纳秒十进制；包含负1ns和超CLR范围时长 |

证明仍为 HMAC-SHA256(key, UTF8(kind + NUL) + 值部分)。对齐100ns且在CLR范围内的值沿用共同编码；Duration用有界来源的BigInteger计算，避免先转long溢出。新增5个独立Python标准库向量与固定 Connector/J9.7.0 的真实 NativeQueryBindings／NativeQueryBindValue 核对，原绑定对象保持不变。

读取器同时识别Java v1／v2，原生kind必须属于v2且具有准确运行时类型；报告单列 `nativeTemporalProofs`。这些输入尚不能直接与C#共同值比较，不能当作已等价、忽略或自行舍入。驱动线上编码、数据库列精度／舍入及实际回读仍待真实配对证明；v2只解决无损记录原输入，不改变服务器预处理、批处理／保存点等未验边界。

## 实际验证及剩余边界

- 读取合同专项21项通过，包含实际CLI新建／拒绝覆盖、重新计算SHA后的内容篡改、错误来源、伪造完整标记、事务及时间次序。测试文件是明确合成样本。
- 原Java采集基础严格JDK21／固定驱动的49个SelfTest通过，内部含新增5向量；共同16向量保持。Spring接线20项通过；显式测试属性 `metarill.paired.test-evidence` 只把本次合成目录复制到全新私有位置，不导出key、覆盖旧记录或新增业务端点。
- 独立CLI实际读取Spring测试链的3命令／5事务设置与边界、最终1／1索引，通过。这是实际生产者生成的文件，但数据库、原执行和Servlet请求是明确替身。
- 独立CLI只读核验V44保留的原C# Program Production/TestServer两条原路由401、零SQL、最终2／2索引，通过；固定Data SHA为89d2a4bbf48d550a985a1c16a627e2db3659209566559f7fce1f868548eb1e55。没有把匿名请求扩展为普通身份或业务SQL通过。

私有证据目录：`.local/sql-artifact-audit-20261102/343478560f3449088d46197dc000ef15`，包括固定预期／摘要、实际CLI回执、关闭索引和测试日志。首轮读取合同测试2个错误来自测试夹具把记录ID变化也用作文件名，已修正为固定索引文件名，未放宽核验器。

本项没有连接数据库、启动真实Web／Docker、创建远端对象或运行性能负载；V37三镜像及已结束授权不变。正式业务SQL／参数语义、物理目标、完整连接路径覆盖、相同业务数据、负载、原前端／跨端及MySQL首版总验收继续待做。最终性能工具完整回归150项：146通过、4个POSIX条件跳过；Java49内部用例含于1个Python测试，专项21项也已包含，不能重复累计。原宿主重建最终17项离线测试通过，原293个输入与原JAR361类／资源逐字保持，仅追加16个采集class；原runtime驱动POM及驱动SHA保持。第一次打包选择器漏写验证码类名末尾s，只运行10项，独立XML总数断言已拦住；修正选择器后17项通过，保留两次日志，没有放宽数量断言。新JAR未部署，原三镜像未替换。
