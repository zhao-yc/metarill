# V742 富文本详情与读取投影回归

2026-11-09。以 de6da757 的 2,945 份固定源码为基线。Windows 生成器第一组实际发现两项 ReadProjectionCompilationTests 失败，旧断言要求详情页直接输出 v-html。

两处断言均更新为检查原详情使用 yzp-rtf-preview 绑定备注，并禁止再次出现直接渲染路径。保留了列表备注排除、状态显示字段、旧 VO 编译，以及表级控件定义和页级覆盖的投影边界。

以一份测试源码覆盖基线，在独立副本离线严格构建。ReadProjectionCompilationTests 与 OriginalUniappDetailTests 共 53 项全部通过，含原两项失败用例。输入逐文件核验。该提交仅调整测试与验收记录，生成器、模板和前端运行载荷仍对应原候选。Windows 六组全量和 GitHub Actions 结果分别核对。
