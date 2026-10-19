param(
 [Parameter(Mandatory=$true)][string]$SourceSettings,
 [string]$DotnetPath = 'dotnet'
)
$ErrorActionPreference='Stop'
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
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
 $process.StandardInput.Write((@{runId=$runId;operation=$Operation;password=$password}|ConvertTo-Json -Compress));$process.StandardInput.Close()
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
$oldSettings=$env:METARILL_FRESH_INSTALL_SETTINGS;$oldDotnet=$env:DOTNET_HOST_PATH
$created=$false;$cleaned=$false;$passed=$false
try{
 $created=$true;Invoke-FreshServer 'create'|Out-Null
 @{RunId=$runId;Database=(New-FreshOptions 'platform');BusinessDatabase=(New-FreshOptions 'business');PackagedDatabase=(New-FreshOptions 'package')}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $settingsPath -Encoding utf8
 $env:METARILL_FRESH_INSTALL_SETTINGS=$settingsPath;$env:DOTNET_HOST_PATH=$DotnetPath
 & $DotnetPath build (Join-Path $repoRoot 'backend/src/Metarill.Api/Metarill.Api.csproj') --nologo --disable-build-servers --verbosity quiet *> (Join-Path $runDirectory 'build.log')
 if($LASTEXITCODE -ne 0){throw '平台编译失败，请检查本轮私有 build.log。'}
 & $DotnetPath test (Join-Path $repoRoot 'backend/tests/Metarill.Generation.Tests/Metarill.Generation.Tests.csproj') --nologo --disable-build-servers --filter 'FullyQualifiedName~FreshInstallMySqlTests' --logger 'trx;LogFileName=fresh-install.trx' --results-directory $runDirectory *> (Join-Path $runDirectory 'test.log')
 if($LASTEXITCODE -ne 0){throw '空库真实验收失败，请检查本轮私有 test.log 与 TRX。'}
 $passed=$true
}
finally{
 $env:METARILL_FRESH_INSTALL_SETTINGS=$oldSettings;$env:DOTNET_HOST_PATH=$oldDotnet
 if($created){Invoke-FreshServer 'cleanup'|Out-Null;$cleaned=$true}
 if($cleaned -and (Test-Path -LiteralPath $settingsPath)){Remove-Item -LiteralPath $settingsPath}
 $password=$null
 if($cleaned){Write-Output '本轮随机账号与3个随机库已全部清理；未操作既有库。'}
}

if($passed -and $cleaned){Write-Output "空库验收通过；编号：$runId，测试结果：$runDirectory"}
