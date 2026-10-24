# 固定的第三方源码归档

`xlsx-0.20.3.tgz` 是 [SheetJS 官方安装说明](https://docs.sheetjs.com/docs/getting-started/installation/nodejs/) 指定 HTTPS 地址的原始 CE 包。官方推荐把归档随项目保存，再用 `file:vendor/xlsx-0.20.3.tgz` 安装，避免公共 npm 的陈旧版本和远端可用性影响。

- 不修改归档内部源码、版本和许可证；不把它当作可清理的构建输出。
- `sheetjs-source.json` 记录官方 URL、取得日期、SHA-256 与实际归档计算出的 SHA-512 SRI。锁文件固定同一 SRI；这不是上游签名证明。
- 原包的 `package/LICENSE`、`package/dist/LICENSE` 原样保留在归档；前端 `licenses/sheetjs.LICENSE` 和 `licenses/sheetjs-dist.LICENSE` 另提供可读副本。原作者署名及归属说明保留在完整源码中。
- 各环境使用 `npm ci --ignore-scripts`，不重新解析 npm 的 `xlsx@0.18.5`。
- 更新需重新核实官方版本、许可、摘要、锁文件、实际 XLS/XLSX/CSV 导入以及当前审计结果，不能仅修改版本说明或忽略告警。
