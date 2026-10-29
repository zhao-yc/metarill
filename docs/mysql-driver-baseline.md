# MySQL驱动显式版本与审计基线

日期：2026-10-29。前基线3536e02通过FreeSql.Provider.MySqlConnector传递取得MySqlConnector2.5.0。Agent实际私有合同重新还原依赖时，NuGet NU1902被warnaserror提升为错误；原build.log和TRX保留，不把这次失败写成产品行为回归或正式库验收。

[官方公告GHSA-473q-m89c-ghf8](https://github.com/advisories/GHSA-473q-m89c-ghf8)列明受影响版本低于2.6.1，修复版本2.6.1；问题涉及无配置CA的VerifyCA/VerifyFull模式下的密码验证顺序。[官方2.6.1发布说明](https://github.com/mysql-net/MySqlConnector/releases/tag/2.6.1)记录修复。没有据此断言本项目连接满足利用条件或凭据已经泄露，也没有读取私有连接进行中间人测试。

Directory.Packages.props现在显式固定MySqlConnector2.6.1，Metarill.Data.csproj直接引用驱动，保持FreeSql及其Provider版本不变。平台与生成独立包都继承这两份源文件；不关闭审计、不忽略NU1902、不降低严格构建、不修改TLS/CA或实际连接配置。新源已重新恢复依赖，后续测试以最终TRX和实际资产解析为准。

升级无需元数据、DDL或回填；已有包须成套更新中央依赖/Data工程并重新还原和构建，不能只复制DLL。MySQL真实连接/事务/并发、文件新截止字段及原TLS部署配置仍需正式回归，构建通过不能替代；首版/MySQL整体和其他数据库支持状态不变。此公告不代表完成仓库级安全扫描。


最终全量相关回归零失败；Agent351通过/25实库条件跳过，私有生成合同实际恢复/严格编译通过。Data、Agent及该子合同的project.assets.json确认解析2.6.1。两个实际生成/完整包构建专项再次通过；上述C32离线批次当时未执行真实连接、事务或TLS中间人用例，不将官方修复声明写成项目专属攻击验证结论。最终证据在.local/platform-calendar-20261029/final-receipt.json。


后续[C33](testing/file-calendar-native-acceptance.md)固定原三表包使用实际2.6.1完成Server-200/MySQL8.4.11连接、CRUD/文件引用事务回滚及重启。TLS模式/CA/中间人、并发与首版完整验收仍未由此验证，原配置未因本轮改变。
