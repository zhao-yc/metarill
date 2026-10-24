param(
 [Parameter(Mandatory=$true)][string]$SourceSettings,
 [string]$DotnetPath = 'dotnet',
 [ValidateSet('Baseline','CrossTable','All')][string]$Scenario = 'Baseline',
 [string]$CredentialBackupRoot = ''
)
$ErrorActionPreference='Stop'
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scenarioProfile=@{Baseline='baseline';CrossTable='cross';All='all'}[$Scenario]
$baselineKinds=@('platform','business','package','nonred')
$crossKinds=@('cross_ss','cross_sl','cross_ls','cross_ll')
$selectedKinds=switch($Scenario){'Baseline'{$baselineKinds};'CrossTable'{$crossKinds};'All'{$baselineKinds+$crossKinds}}
$expectedTestCount=switch($Scenario){'Baseline'{3};'CrossTable'{4};'All'{7}}
$testFilter=switch($Scenario){'Baseline'{'FullyQualifiedName~FreshInstallMySqlTests'};'CrossTable'{'FullyQualifiedName~FreshCrossTableMySqlTests'};'All'{'FullyQualifiedName~FreshInstallMySqlTests|FullyQualifiedName~FreshCrossTableMySqlTests'}}
$propertyNames=@{platform='Database';business='BusinessDatabase';package='PackagedDatabase';nonred='NonRedundantDatabase';cross_ss='CrossStringStringDatabase';cross_sl='CrossStringLongDatabase';cross_ls='CrossLongStringDatabase';cross_ll='CrossLongLongDatabase'}
$backupDirectory=$null
$runId=[Guid]::NewGuid().ToString('N').Substring(0,16)
$runDirectory=Join-Path $repoRoot ".local/fresh-install/$runId"
$settingsPath=Join-Path $runDirectory 'settings.json'
$source=Get-Content -Raw -LiteralPath $SourceSettings | ConvertFrom-Json
$baseline=[System.Data.Common.DbConnectionStringBuilder]::new()
$baseline.set_ConnectionString($source.Metarill.BusinessDatabase.ConnectionString)
if($source.Metarill.BusinessDatabase.Provider -ne 'mysql' -or $baseline.Item('server') -notin @('deve-server.yinzon.com','192.168.31.200')){throw '空库验收仅允许已核验 Server-200 MySQL。'}
New-Item -ItemType Directory -Path $runDirectory | Out-Null
$password=[Guid]::NewGuid().ToString('N')+[Guid]::NewGuid().ToString('N')
$remoteSource=[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $repoRoot 'tools/acceptance/server200-fresh-mysql.py')))
$remoteCommand="python3 -c 'import base64;exec(base64.b64decode(`"$remoteSource`"))'"

# 不经本地 shell 拼接命令；随机密码只通过 SSH 标准输入传输。
function Invoke-FreshServer([string]$Operation){
 $start=[Diagnostics.ProcessStartInfo]::new('ssh')
 $start.ArgumentList.Add('server-200');$start.ArgumentList.Add($remoteCommand)
 $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
 $process=[Diagnostics.Process]::Start($start)
 $process.StandardInput.Write((@{runId=$runId;operation=$Operation;password=$password;scenario=$scenarioProfile}|ConvertTo-Json -Compress));$process.StandardInput.Close()
 $outTask=$process.StandardOutput.ReadToEndAsync();$errTask=$process.StandardError.ReadToEndAsync();$process.WaitForExit()
 $outText=$outTask.GetAwaiter().GetResult();$errText=$errTask.GetAwaiter().GetResult()
 if($process.ExitCode -ne 0){throw "Server-200 空库管理失败（$Operation），$errText"}
 $process.Dispose();return ($outText|ConvertFrom-Json)
}

# 保留基础连接的 TLS/超时设置，只替换本轮随机库与受限账号。
function New-FreshOptions([string]$Suffix){
 $connection=[System.Data.Common.DbConnectionStringBuilder]::new();$connection.set_ConnectionString($source.Metarill.BusinessDatabase.ConnectionString)
 $connection.set_Item('database',"metarill_fresh_${runId}_$Suffix")
 foreach($key in @('user id','uid','user','username','pwd','password')){if($connection.ContainsKey($key)){$connection.Remove($key)|Out-Null}}
 $connection.set_Item('user id',"mtf_$runId");$connection.set_Item('password',$password)
 return @{Provider='mysql';ExpectedDatabase="metarill_fresh_${runId}_$Suffix";ConnectionString=$connection.get_ConnectionString()}
}
$oldSettings=$env:METARILL_FRESH_INSTALL_SETTINGS;$oldDotnet=$env:DOTNET_HOST_PATH;$oldEvidence=$env:METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY
$created=$false;$cleaned=$false;$passed=$false;$createdState=$null
$startedAt=[DateTimeOffset]::UtcNow.ToString('o')
try{
 $created=$true;$createdState=Invoke-FreshServer 'create'
 $freshSettings=[ordered]@{RunId=$runId;Scenario=$scenarioProfile;InstancePin=$createdState.instance}
 foreach($kind in $selectedKinds){$freshSettings[$propertyNames[$kind]]=New-FreshOptions $kind}
 $freshSettings|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $settingsPath -Encoding utf8
 # 只备份本轮受限凭据，不复制原连接账号；成功清理后补写凭据已撤销的说明。
 if(-not [string]::IsNullOrWhiteSpace($CredentialBackupRoot)){
  $backupRoot=[IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($CredentialBackupRoot))
  $backupDirectory=[IO.Path]::GetFullPath((Join-Path $backupRoot $runId))
  if(-not $backupDirectory.StartsWith($backupRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)-or(Test-Path -LiteralPath $backupDirectory)){throw '凭据备份目标不合法或已存在，未覆盖。'}
  [IO.Directory]::CreateDirectory($backupDirectory)|Out-Null
  Copy-Item -LiteralPath $settingsPath -Destination (Join-Path $backupDirectory 'settings.json')
  @("# Metarill 本轮空库验收私有配置","","编号：$runId；场景：$Scenario。仅用于已核验Server-200本轮随机库和受限临时账号，不含容器管理员密码。","继承原开发连接的网络/TLS选项；此文件不可提交或公开。当前尚待清理，后续仅用于本轮重试或审计。")|Set-Content -LiteralPath (Join-Path $backupDirectory '说明.md') -Encoding utf8
 }
 $env:METARILL_FRESH_INSTALL_SETTINGS=$settingsPath;$env:DOTNET_HOST_PATH=$DotnetPath;$env:METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY=$runDirectory
 & $DotnetPath build (Join-Path $repoRoot 'backend/src/Metarill.Api/Metarill.Api.csproj') --nologo --disable-build-servers --verbosity quiet *> (Join-Path $runDirectory 'build.log')
 if($LASTEXITCODE -ne 0){throw '平台编译失败，请检查本轮私有 build.log。'}
 & $DotnetPath test (Join-Path $repoRoot 'backend/tests/Metarill.Generation.Tests/Metarill.Generation.Tests.csproj') --nologo --disable-build-servers --filter $testFilter --logger 'trx;LogFileName=fresh-install.trx' --results-directory $runDirectory *> (Join-Path $runDirectory 'test.log')
 if($LASTEXITCODE -ne 0){throw '空库真实验收失败，请检查本轮私有 test.log 与 TRX。'}
 # 命令成功不等于实际执行，所选场景必须全部通过，逐条结果也不能包含跳过。
 [xml]$trx=Get-Content -Raw -LiteralPath (Join-Path $runDirectory 'fresh-install.trx')
 $counters=$trx.TestRun.ResultSummary.Counters
 if([int]$counters.total -ne $expectedTestCount -or [int]$counters.executed -ne $expectedTestCount -or [int]$counters.passed -ne $expectedTestCount -or [int]$counters.failed -ne 0 -or [int]$counters.notExecuted -ne 0){throw '首次安装场景未全部实际通过，不能计为验收成功。'}
 $results=@($trx.TestRun.Results.UnitTestResult)
 $crossResults=@($results|Where-Object {$_.testName -like '*FreshCrossTableMySqlTests*'})
 $expectedCross=if($Scenario -eq 'Baseline'){0}else{4}
 if($results.Count -ne $expectedTestCount -or @($results|Where-Object {$_.outcome -ne 'Passed'}).Count -ne 0 -or $crossResults.Count -ne $expectedCross){throw '逐项结果与所选场景不符，不能计为验收通过。'}
 $passed=$true
}
finally{
 $env:METARILL_FRESH_INSTALL_SETTINGS=$oldSettings;$env:DOTNET_HOST_PATH=$oldDotnet;$env:METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY=$oldEvidence
 if($created){$cleanup=Invoke-FreshServer 'cleanup';if(-not $cleanup.cleaned -or -not $cleanup.independentAbsenceVerified -or ($null -ne $createdState -and $cleanup.databaseCount -ne $selectedKinds.Count)){throw '所选随机库与临时账号清理未通过独立核验。'};$cleanup|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $runDirectory 'cleanup.json') -Encoding utf8;$cleaned=$true}
 if($cleaned -and (Test-Path -LiteralPath $settingsPath)){Remove-Item -LiteralPath $settingsPath}
 if($cleaned -and $backupDirectory){@("","本轮临时账号与自有随机库已于$([DateTimeOffset]::UtcNow.ToString('o'))完成独立清理核验，以上连接凭据已撤销，不再有效。")|Add-Content -LiteralPath (Join-Path $backupDirectory '说明.md') -Encoding utf8}
 $password=$null
 if($cleaned){Write-Output '本轮所有权清单内的随机库和临时账号已清理；未操作既有库。'}
}

if($passed -and $cleaned){
 @{schemaVersion=1;runId=$runId;server='Server-200';address='192.168.31.200';startedAt=$startedAt;completedAt=[DateTimeOffset]::UtcNow.ToString('o');scenario=$scenarioProfile;passed=$expectedTestCount;failed=0;skipped=0;createdDatabaseCount=$selectedKinds.Count;createdTemporaryAccountCount=1;allTemporaryObjectsCleaned=$true;credentialFileRemoved=(!(Test-Path -LiteralPath $settingsPath))}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $runDirectory 'result.json') -Encoding utf8
 Write-Output "空库验收通过；编号：$runId，测试结果：$runDirectory"
}
