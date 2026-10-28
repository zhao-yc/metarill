# 原按钮规则对照

`vectors.json` 是C#测试与Java原实现共用的61组通用输入/预期结果。`ButtonVisibilityOracle.java` 通过反射调用原 `ButtonVisibilityBusiness.matchesRule`，不复制规则算法，不启动Spring或连接数据库。

JDBC日期、时间和时间戳样本以 `$jdbcDate`、`$jdbcTime`、`$jdbcTimestamp` 标签保存；两个测试夹具各自还原真实类型。标签不属于生产元数据或请求协议。

## 执行

C#在仓库根目录执行：

```powershell
dotnet test backend/tests/Metarill.Data.Tests/Metarill.Data.Tests.csproj --filter FullyQualifiedName~ButtonVisibilityRuleTests -warnaserror
```

Java需要另行准备原YZP生成样板的实际 `target/classes` 和依赖JAR目录，并使用与原类兼容的JDK；不能用重写的C#算法生成所谓Java结果：

```powershell
# 将路径替换为已核定的原Java编译产物与依赖目录；输出放独立临时目录。
$oracleClasspath = '<原样板target/classes>;<依赖JAR目录>/*;<独立输出目录>'
javac -encoding UTF-8 -cp $oracleClasspath -d '<独立输出目录>' tools/acceptance/button-visibility/ButtonVisibilityOracle.java
java -Dfile.encoding=UTF-8 -cp $oracleClasspath ButtonVisibilityOracle tools/acceptance/button-visibility/vectors.json
```

失败直接非零退出；成功逐行输出样本名和比较结论，不输出业务数据或配置。本轮实际执行还核对原源码与冻结模板、调用类与原JAR一致，来源和摘要写入私有验收回执。向量通过不代表真实宿主登录、SQL/关系装载、事务并发或设备验收；边界见[专项说明](../../../docs/button-visible-rules.md)。
