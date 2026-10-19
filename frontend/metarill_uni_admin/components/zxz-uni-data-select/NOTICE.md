# 来源和修改说明

- 组件：zxz-uni-data-select 1.0.20，2024-05-09 发布。
- 作者公开署名：182***@qq.com；[作者发布页](https://ext.dcloud.net.cn/plugin?name=zxz-uni-data-select) 的“许可协议”明确列为 MIT 协议，2026-10-19 核验。
- 原包来源：原 YZP 的 `uni_modules/zxz-uni-data-select`，导入前各文件 SHA-256 见前端根目录 `SOURCE-MANIFEST.json`。原 `package.json` 未填写 license/repository，未将空值当作授权；使用的是作者发布页的明确声明。
- 原作者说明基于 [DCloud uni-data-select](https://ext.dcloud.net.cn/plugin?id=7993) 二次开发。DCloud uni-ui 的 Apache-2.0 许可副本保留在 `LICENSE.upstream`，官方来源为 https://github.com/dcloudio/uni-ui 。
- Metarill 的修改：调整到共享 `components/zxz-uni-data-select` 目录，补充源码来源注释、MIT 正文及本说明，并清理原说明文档的行尾空白；搜索、单选、多选、禁用、清空、事件与样式沿用原实现。此共享文件由所有原组件引用复用，未手改可再生成页面。
- 分发时保留本目录的 `LICENSE`、`LICENSE.upstream`、本说明与原源码已有的署名/归属注释；Metarill 的 Apache-2.0 不替代组件自身许可。

- 2026-10-19：增加默认关闭的受控 `remote` 模式，提供独立已选回显、底部加载通知和加载状态；原本地过滤/选择模式保持原实现。网络、权限、分页和乱序隔离在 Metarill 外层 wrapper/runtime 管理，原 MIT/Apache-2.0 许可继续随目录分发。
