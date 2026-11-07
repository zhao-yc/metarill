# 前端依赖修复记录

## SheetJS CE 0.20.3（2026-10-19）

实际 `npm audit --json` 原报告只有一个高危直接依赖 `xlsx@0.18.5`，对应两个公告：

| 公告 | 官方受影响范围 | 官方修复要求 |
| --- | --- | --- |
| [CVE-2023-30533](https://cdn.sheetjs.com/advisories/CVE-2023-30533) 原型污染 | 至 0.19.2 | 0.19.3 或更高 |
| [CVE-2024-22363](https://cdn.sheetjs.com/advisories/CVE-2024-22363) ReDoS | 至 0.20.1 | 0.20.2 或更高 |

按 [官方安装方式](https://docs.sheetjs.com/docs/getting-started/installation/nodejs/) 升级至 CE 0.20.3，保留官方原始归档，以本地 `file:vendor/xlsx-0.20.3.tgz` 和锁文件 SRI 安装；公开 npm 的旧发布不作为更新来源。归档 SHA-256 为 `8dc73fc3b00203e72d176e85b50938627c7b086e607c682e8d3c22c02bb99fe8`，大小 2,409,319 字节。归档 `package.json` 确认名称 xlsx、版本 0.20.3、作者 sheetjs、Apache-2.0、无运行时依赖；许可及来源说明随前端保留。摘要由官方 HTTPS 下载后的实际字节计算，未声称核实独立的上游签名。

没有替换库、修改 `chooseImportFile` 或页面交互，仍使用 `import * as XLSX from 'xlsx'`、`XLSX.read(binary,{type:'binary',cellDates:true})` 和 `XLSX.utils.sheet_to_json`。新增 5 项回归真实调用安装后的 ESM 解析器及原共享方法，验证 XLSX、BIFF8 XLS、UTF-8 CSV 的中文、数字、零值、布尔、日期、空值省略、第一工作表选择及损坏 ZIP 不提交导入；并检查归档、SRI、安装版本一致。CI 前端作业先执行 `npm ci --ignore-scripts`，其他后端和派生检查保持原状。

对同一生成输入另做 0.18.5 与 0.20.3 ESM 对照：姓名、数值、布尔在三种格式均一致；在本机 Windows / Asia-Shanghai / Node 24 环境，旧版读取 XLSX 日期比新版偏早 43 秒（旧 `2026-10-18T23:59:17Z`，新 `2026-10-19T00:00:00Z`，新版匹配输入日期）；BIFF8 和 CSV 该样例的日期输出一致。官方说明 [0.20.0 调整了日期处理](https://docs.sheetjs.com/docs/csf/features/dates/)，本次保留修复版正常解析，不人为恢复旧偏差，也不声称所有既有文件逐字节输出相同。

验证：真实 `npm ci --ignore-scripts` 成功；完整前端测试 213 项通过，0 失败、0 跳过；升级后 `npm audit --json` 的漏洞对象为空，所有级别计数为 0。本次没有采用告警忽略、严重性降级或伪造审计响应，也没有宣称这些业务样例代替漏洞利用样本验证。没有扩大修改 CryptoJS 等其他依赖；其原有停止维护提示保持可见。HBuilderX 和浏览器由主分支集成者统一验证，本切片未运行。
