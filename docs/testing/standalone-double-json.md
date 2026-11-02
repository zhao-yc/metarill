# 独立宿主 double JSON 登记修正（A32）

日期：2026-11-02。A31固定03177fb的真实Production/Web新增回执暴露相邻double值合并：0410000000000000变为040FFFFFFFFFFFFF。原结构反查与缺列数据库默认已通过，不能将此错归为默认DDL或decimal范围问题；A31仍按整条失败留证并已清理。

## 原因与源头

平台API的MVC及最小接口已使用JsonDefaults登记FiniteDoubleJsonConverter。独立应用正式／开发宿主模板仅在MVC登记StringLongJsonConverter，默认浮点响应格式会在该运行时把已知相邻值合并。已有转换器通过逐位往返校验及必要的G17输出规避该缺口，并拒绝非有限／非零下溢输入。

修正backend/templates/standalone/Program.cs.tpl与DevelopmentProgram.cs.tpl：在MVC原ID转换器旁登记已有FiniteDoubleJsonConverter，最小接口同时登记此浮点转换器。日期、ID及其它JSON配置保持，原路由、身份、错误响应策略、DTO存在性、原前端／模板样式和操作均保持；不更换整套JSON选项，不手改任何已冻结包或可覆盖的业务产物。

## 回归及证据边界

新增StandaloneDoubleJsonTests，从两个实际模板截取真实JSON登记片段并严格编译，在隔离HTTP宿主执行MVC读取／提交及最小接口读取。不是复制另一份预期转换器配置；验证两相邻位型、MVC字符串ID与原最小接口整数ID，以及溢出／非零下溢／NaN的明确拒绝。不连接数据库或注入业务身份，不能代替完整应用正式验收。

修改前两模板各一项实际HTTP失败，均复现与A31相同的预期／实际位型差异；原日志／TRX在.local/standalone-double-json-20261102保留。修正后严格Release组合24通过、0失败、0跳过，包含两个真实HTTP登记片段、真正Full从自身源码独立严格构建、六个原匿名拒绝入口、租约边界与四个更新包数值源码闭包检查。程序使用原有限转换器，未降低位型或输入断言。

## 生成、更新及回退

新独立应用需由当前模板重新打包；既有应用按原助手／CLI的运行时更新预览，核对受管backend/Application.Host/Program.cs变更后应用，并保留所有手写文件及私有配置。无需DDL或数据回填，平台主服务已有登记无需补写。回退须使用原配套包／清单；回退会恢复该响应缺陷，不能称作数值精度验收通过。

原A31唯一库／账号及合成storage均已独立核清、租约结束；修正后的数值默认／CRUD／统计／原Excel／事务／重启全链需要新来源、新包及新精确授权。未执行浏览器／真机或性能，不计MySQL首版总验收完成。

补充原更新动作回归时，前端文件一项通过、后端Program一项先失败：测试Rehash辅助函数只维护前端清单，错误要求Program具有前端归属。仅校正测试夹具为按实际归属维护摘要，产品更新器未修改；原失败保留；最终运行时更新两项及原配置／partial／AppID保护一项，共3通过、0失败、0跳过，实际应用Program后元数据保持。该组与上述24项分开记录。723个原前端／元数据／生成／partial文件字节不变。
