# 双端原生验收资源与显式初始化准备

2026-11-01，V21。为V20新Java登录宿主与V17 C#完整包准备独占运行前提；本批没有创建数据库、账号或Redis键，没有执行真实SQL。资源编排、播种、真实登录/权限/业务和性能仍未完成，不因工具门禁通过而转记首版验收。

## 双库白名单与Redis来源

`tools/acceptance/server200-fresh-mysql.py`新增独立`paired`场景，仅生成`metarill_fresh_<16位运行号>_paired_java`与`_paired_csharp`，临时账号仍为`mtf_<运行号>@%`且只授权这两个库。不允许调用方选择后缀，也没有把这两个库加进旧`all`；旧场景及旧授权范围不扩大。未来执行须先固定协调器和精确对象计划、取得对应授权，不能凭新增场景直接运行，更不能借用待批C19单库租约。

全部18项MySQL协调器离线回归通过、零失败/跳过；新增3项覆盖双库记录不能按旧场景删除、第二库创建失败只清第一库、实例变更禁止DROP。原成功循环另验证paired实际只生成两库/两份库级GRANT，旧all仍为八库。这些使用内存MySQL桩，不是新双库实测。

Server-200先核主机名/IP再逐条使用显式Unix socket，只读核对既有`yinzon-redis`的运行和镜像身份，并执行`redis-server --version`：Redis7.4.11，linux/amd64镜像`sha256:ff02b58f971e7d7d156a1267e283fcbbeee91773b6aa36c49dac28ecfe28eadf`。没有读键、环境或配置，也未获取其密码。后续优先使用独占临时Redis实例，DB0及原application-name前缀保持，不能用共享实例空白键空间的假设代替隔离。

## 固定初始化输入与账号语义

Java输入来自e97ca4f固定JAR的14份配置/文件DDL和project-config JSON，加原347来源的三份账号DDL、原V8日志DDL、原业务三表DDL，共20项（19 SQL+1 JSON），逐字固定；业务DDL SHA为`6f4fa61800f04505868293f79df5f474f28645a64a4251db6e243dac712af772`，仍由原ConfigTableDbBusiness生成。C#的project/files/security三份元数据来自V17原包，逐字核对。没有手写或精简三表、页面、父子或字段模型。

原接口`isDisabled=1`语义为“启用”；Java持久化is_disabled=1，而C#已有ManagerCredentialValue适配将接口1转换为内部禁用标记0。后续播种必须按相同有效账号状态对齐，不能为追求原始列值相同而把一端停用，也不改接口/存储适配规则。角色及页/按钮有效权限仍要正式核对。

## 外置Java显式配置入口

新增`tools/performance/PairedJavaConfigurationBootstrap.java`，独立编译为4个主入口class，使用固定应用Jar的真实框架类。它不注册Bean，不进入普通SampleApplication启动流程，也没有修改现有JAR/镜像。原配置读取器、配置同步服务及原DDL均被复用；未手写元数据入库或基础CRUD。

| 模式 | 合同 | 本批实际状态 |
| --- | --- | --- |
| preview-offline | 核20项输入、原Jar资源/三表3页3按钮，计算确定性计划摘要；不读数据库凭据 | 本机及200禁网容器实际成功，输出**预期**22表，非实际数据库结构 |
| preview | 本轮approved/running租约、精确运行号/库/账号/200端点；连接后核数据库/实例pin并只读表数 | pending入口本机与200均在lease阶段拒绝；真实数据库预览未执行 |
| apply-initial | 同上，且实际0表、ExpectedPlanSha256等于已预览计划；执行后实际表集合必须精确22表 | pending入口在lease阶段拒绝；实际DDL、元数据写入及22表核验均未执行 |

清单用途固定`paired-runtime-bootstrap-v1`，32位runId的前16位决定唯一Java库及mtf账号；与MySQL资源工具的`paired`名称对应。来源清单必须精确20项，路径/摘要、原Jar内配置、原业务DDL身份和完整目录均验证。元数据捕获为内存快照，执行时不重新解释可变项目JSON。SQL前再次校验被执行脚本的摘要；只有授权空库安装走原同步服务，不向已有配置库运行重建。

真实分支凭据仅取YZP_SAMPLE_JDBC_URL/DB_USER/DB_PASSWORD私有环境，URL限定192.168.31.200:3306及精确库/账号；不接收命令行口令。数据库与@@hostname/@@server_uuid必须匹配本轮pin。失败输出有界阶段和异常类型，不回显连接/SQL正文，不自动重试或宣称可回滚MySQL DDL。该入口只解决初始结构/配置，不负责账号/角色/业务播种或长期结构升级。

### 复核方式与实际验证

```text
javac --release 21 -proc:none -cp <固定应用classes及依赖> -d <独占class输出> tools/performance/PairedJavaConfigurationBootstrap.java tools/performance/PairedJavaConfigurationBootstrapSelfTest.java
java -cp <同一固定类路径及外置class> com.yinzon.samplehost.PairedJavaConfigurationBootstrap preview-offline <本轮清单.json>
```

新清单需要Version=1、Purpose、RunId、ExpectedDatabase、BundleRoot、SourcesManifestSha256、AuthorizationStatus及LeaseStatus；真实分支另需InstancePin，apply须ExpectedPlanSha256。未批准时保持pending/not-started；不能复制示例标记当作得到授权。

JDK21实际独立编译，10项自检使用真实原配置读取器/固定输入，覆盖20来源/22表计划、确定性摘要、pending/ended、错误库、源变化、更新自报摘要后仍不能替换原DDL、越界清单项、同义但不同原Jar字节的JSON。自检无数据库连接。实际CLI另验证离线成功与pending真实preview/apply两个入口在凭据前拒绝，不混为真实SQL测试。

Server-200固定Java329f43e镜像内，通过PropertiesLauncher加载只读挂载的外置class，实际运行上述三种模式。三个容器禁网、只读根、非root、丢弃能力、0.5CPU/256MiB/64PID，离线结果及两个预期拒绝正确；逐个以新Docker命令核验容器不存在。未启动Spring业务宿主或连接MySQL/Redis。源码/class与实际Linux载荷摘要逐字吻合，776项原页面/模板/元数据/生成保护不变。

私有根`E:\git\metarill\.local\paired-native-preparation-20261101`保存bootstrap-bundle、原来源摘要、真实编译/10项自检/CLI日志、offline-java-plan、redis-image-readonly、bootstrap-final-proof及Linux三个入口回执；Server-200本轮只读载荷/日志保留，未创建任何业务数据库或账号。

## 仍需完成

准备并固定一次性容器/网络/文件目录创建与清理协调器、私有Redis配置及同资源/回环端口；按精确新范围授权后，实际空库预览/安装/物理结构核验、两端同逻辑账号/角色/页权播种、真实登录/撤权/期限、原关系/父子/文件/三动作、SQL/索引/事务和同条件负载。当前没有完整正式执行器或新的实库通过，不提交未经完整准备的数据库授权问题；V08及MySQL首版总验收保持未完成。

### V22 后续资源子工具

独占Redis/双宿主/网络的创建、一次启动和精确清理工具已接通，见[容器资源合同](paired-container-resources.md)。本机84项与200同源码18项离线检查完成，真实Docker仍未运行；上层初始化/播种/业务协调器未完成，不改变本页实库待验边界。

### V24 普通身份种子

同逻辑三类普通账号的双端存储/原密码摘要适配与受限播种回读函数已接通，见[身份播种合同](paired-identity-seed.md)。10项离线与固定原Java/C#密码类实际调用完成；真实MySQL/Redis/HTTP、账号有效权限和完整上层协调器仍待。
