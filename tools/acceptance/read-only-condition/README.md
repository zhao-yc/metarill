# 原只读条件对照

`vectors.json` 含64组公开输入和预期：`writable`为允许，`readonly`为只读拒绝，`invalid`为无法可靠解释。C#测试与 `ReadOnlyConditionOracle.java` 共用此文件，Oracle直接调用原 `YzpAgentReadOnlyGuard.requireWritable`，不重写算法、不启动Spring或连接数据库。

C#：

```powershell
dotnet test backend/tests/Metarill.Data.Tests --filter FullyQualifiedName~ReadOnlyConditionRuleTests
```

Java另需原YZP样板的实际 `target/classes`、依赖JAR及JDK。先核对原源码/模板与执行类/JAR一致，再执行：

```powershell
$oracleClasspath = '<原样板target/classes>;<依赖JAR目录>/*;<独立输出目录>'
javac --release 21 -encoding UTF-8 -cp $oracleClasspath -d '<独立输出目录>' tools/acceptance/read-only-condition/ReadOnlyConditionOracle.java
java -Dfile.encoding=UTF-8 -Dsun.stdout.encoding=UTF-8 -Dsun.stderr.encoding=UTF-8 -Dstdout.encoding=UTF-8 -Dstderr.encoding=UTF-8 -cp $oracleClasspath ReadOnlyConditionOracle tools/acceptance/read-only-condition/vectors.json
```

每个向量输出实际和预期结果，有差异最终退出失败。2026-10-29已在JBR25.0.2实际执行全部64组；不把C#自测或编译当作原Java对照。范围与未验收边界见 [只读条件专项](../../../docs/button-readonly-conditions.md)。
