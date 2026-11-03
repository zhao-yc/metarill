# V61 原Cap配置与二次校验恢复

日期：2026-11-03；起点4f8b55b。属于原登录能力恢复的一个子集，账号／IP失败锁仍待补。此项不要求复用旧站点密钥，不修改原登录页HTML／样式，不把合成Cap当作真实外部服务验收。

## 原来源及先失败

依据固定b6d64fb的AdminCapCaptchaProperties／Verifier、原九方法登录Controller及公开回归：默认关闭；启用后缺token提示“请先完成安全验证”，远端拒绝提示“安全验证失败，请重试”，缺配置／异常提示“安全验证暂不可用，请稍后再试”。公开GET配置只返回enabled及apiEndpoint，私钥不下发。原表单第三字段为captchaVerification，siteverify使用JSON secret／response两字段。

新真实Full宿主用私有合成启用配置，先实际复现1失败：公开配置仍硬编码false。此失败及后续可选form字段遗漏导致的1失败／157通过均保留；未放宽缺字段用例，修正为可选读取。

## 后端与配置

共享LoginCapVerifier使用HttpClient，只在启用后校验；开关默认false，启用配置缺失或地址非法时明确拒绝，不退回免验证。平台动作和完整包实际HTTP边界在账号查询前执行同一实现；密码／账号失败审计不因验证码拒绝而提前写入。Cap拒绝沿原HTTP200／R500固定提示，调用方取消继续传播。

平台从Metarill:Authentication:Cap读取，完整包从Authentication:Cap读取。两者字段均为Enabled／ServerUrl／SiteKey／SecretKey；私钥只放忽略的本机设置或环境变量，对应Metarill__Authentication__Cap__SecretKey／Authentication__Cap__SecretKey。示例只提供空值，不能把旧Java或其他项目密钥复制进公开工程。正常启动不执行DDL，此项无新增表或数据迁移。

Host的真实HttpClient禁用重定向和Cookie，固定10秒超时。地址仅允许HTTP／HTTPS且不含userinfo、query或fragment；返回体有界、只接受唯一success的布尔true。传输、坏JSON、过大响应或远端正文不会进入用户提示／异常链；公共配置不含私钥，选项也不通过JSON或record文本输出私钥。

## 原前端与生成模板

原平台页面已有完整Cap交互，不修改其页面。生成模板缺少该执行流程，本任务逐字复用原loadCapConfig／ensureCapConfig／solveCapToken三个方法，保留进度、实例重用、reset及一次性token；固定项目、服务端字符串后台ID、密码清空、原事件和跳转继续沿用原独立应用合同。整个template与style对4f8b55b逐字保持，原H5固定哈希测试未刷新。

生成页也使用原form第三字段；配置／验证码失败时不提交密码，统一恢复按钮。H5使用既有cap-widget0.1.50；App／小程序不引入浏览器Worker，启用Cap时与原平台一样明确拒绝，不能静默绕过。默认关闭时保留原登录能力。

已有工程配套同步Security、Program／配置示例和登录模板；更新器要求当前Web候选保留LoginCapVerifier.cs完整闭包，删除源码及归属仍拒绝。配置示例不覆盖开发者现有私有配置／AppID，实际启用需由部署者单独配置。

## 实际验证及边界

共享Cap26项及安全整组165项均通过、零失败／跳过。真实Full宿主对本机合成Cap进行form／JSON：通过才抵达无库身份拒绝，拒绝保持原R500，302不被跟随、密钥不进入查询或返回；平台实际动作也以受控传输证明在身份查询前拒绝。完整认证／原前端打包179项通过、前端运行时整组356项通过，均零失败／跳过；各组有重叠，不合计为总验收。

HBuilderX5.34.2026102909实际生成Vue3私有完整包，用用户此前提供的AppID，仅本机publish web、webHosting=false：编译及导出成功，52产物文件固定，原HTML／样式保持，结束后仅关闭本轮导入工程。命令为D:/HBuilderX/cli.exe publish web --project E:/git/metarill/.local/login-cap-web-20261103/application/frontend --webTitle "Metarill Cap 验收" --webHosting false；无持续服务，无云托管／商店发布。技能的hbx-cli.sh只提供macOS默认路径，本次使用仓库已核实的Windows CLI。

平台Release严格编译零警告／错误；元数据整组576通过／4项实库条件跳过／零失败。没有访问真实Cap站点或使用真实密钥，未进行真实浏览器解题、App／小程序编译或设备验收，也未执行新MySQL资源。原账号／IP失败锁及最终提交CI仍待，不能把此项标成原登录或MySQL首版全部完成。私有日志在.local/paired-sql-log-review-20261103及.local/login-cap-web-20261103。
