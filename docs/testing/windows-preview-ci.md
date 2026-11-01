# Windows CI私有预览真实路径夹具修复

2026-11-01，C89；起点71ce2fc。只修合成传输测试的临时根规范化，原代理代码、私有目录守卫、前端／模板／生成产物均未修改。本机Windows Node24.17.0的原两文件命令最终4通过／0失败／0跳过；远端新CI结果另行核对，不把本机绿色计为完整CI通过。

## 真实失败与复现

公开GitHub API确认17d38cc的运行41317635286：前端成功，Windows后端在“核对管理页面私有预览传输边界”失败；Ubuntu后端当时仍执行全部测试。公开注释只有退出码，随后经正常ego lite GitHub页面读取原失败日志，没有提取凭据、取消或重跑旧作业；本次只读空间34已关闭。

原日志为2通过／1失败，serve-runtime-browser.test.mjs的createRuntimeBrowserGateway在第22行报“原浏览器目录不允许链接或重解析跳转”。本机相同原命令3项通过，不能据此假定CI误报。新增本轮拥有目录下的实际junction／符号链接模拟借用临时根别名，原守卫上复现同样错误，红灯为3通过／1失败。日志没有给出具体原始Temp路径，不猜定CI使用了哪一种Windows缩略名或重解析点。

## 修复与保持的边界

测试fixtureRoot只将自己通过mkdtemp新建的目录交给realpath取得规范真实路径，再在其下建立静态和私有目录。不会按字符串忽略大小写、不扩大路径白名单、不移除链接检查，也不修改createRuntimeBrowserGateway。

新增实际文件系统回归：借用受控目录别名创建夹具后，规范路径满足原代理；若显式把credentialDirectory改回别名，原代理仍必须拒绝。临时链接目标及工程都在本轮自有根内，完成后按明确边界清理；不访问真实浏览器会话、数据库或凭据。

实际命令：`node --test tools/acceptance/serve-management-browser.test.mjs tools/acceptance/serve-runtime-browser.test.mjs`。Windows原生首次3项通过；受控别名新增红灯3／1，修复后4／0、零跳过。正常代理字节／头／正文、未知运输故障／同批次恢复、秘密不进日志及静态越界拒绝仍实际执行；这些是明确合成传输，不计C88原页面或其它正式业务新增通过。

私有证据：.local/windows-preview-ci-20261101下first.log、red.log、green.log及github-windows-error.json。原运行和失败按17d38cc保留，新提交会按现有push规则启动CI，不能修改旧结果或宣布V10完整通过。
