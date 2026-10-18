# 源码与元数据来源基线

## 脱敏元数据参考

- 来源：YZP 的 yzp-config/project-config.json；源配置仅含配置记录，不导出任何业务表行数据。
- 原始 SHA-256：`4f8482957f1be1d7e860a221fa3baee8ad1775694affe1da66666b28dcd01da4`。
- 脱敏参考 SHA-256：`77b15b99b5d46fc0e4cc5164ce4cd3f85e25acff495eb0e69d6cf6a151a2d374`。
- 保留通用配置、项目、管理员、角色元数据及旧表/页面 ID；删除客户、合同、付款、发票、银行和监控对象及其页面、字段、菜单、按钮、子表和索引引用。
- 清除历史时间戳、无效直接引用 0 处；去除 YZP 项目显示品牌，保留兼容字段名。
- platform-source.json 仅是迁移参考，不是种子数据、生产数据或可执行初始化配置。菜单两表保留为源参考，运行配置应转成单表树。
- 不包含项目连接记录、账号密码、数据库行数据、证书、AppID、部署配置或历史仓库。

| 配置集合 | 原始数量 | 保留数量 |
| --- | ---: | ---: |
| configButtons | 36 | 34 |
| configChildren | 22 | 11 |
| configColumns | 369 | 262 |
| configFirstMenus | 3 | 2 |
| configIndexColumns | 7 | 0 |
| configIndexes | 3 | 0 |
| configPageColumns | 597 | 410 |
| configPages | 41 | 26 |
| configSecondMenus | 28 | 21 |
| configTables | 29 | 20 |
| menuCustomPages | 9 | 8 |
| yzAdminTypes | 1 | 1 |
