param([ValidateSet('start','stop','logs','initialize','test','generate','frontend','all')][string]$Action='start')
$ErrorActionPreference='Stop'
$metarillRoot=Split-Path $PSScriptRoot -Parent
$dotnet=Join-Path $metarillRoot '.tools/dotnet/dotnet.exe'
if(-not(Test-Path -LiteralPath $dotnet)){$dotnet=(Get-Command dotnet -ErrorAction Stop).Source}
$local=Join-Path $metarillRoot '.local'
New-Item -ItemType Directory -Path $local -Force|Out-Null
$pidFile=Join-Path $local 'backend.pid.json'
$dll=Join-Path $metarillRoot 'backend/src/Metarill.Api/bin/Debug/net10.0/Metarill.Api.dll'
# 所有入口共用脚本，只有本项目进程可被停止；不终止不明端口占用。
switch($Action){
'generate'{
 foreach($target in @('platform','demo')){
 & $dotnet run --project (Join-Path $metarillRoot 'backend/src/Metarill.Cli') -- generate --metadata (Join-Path $metarillRoot "backend/metadata/$target.json") --output (Join-Path $metarillRoot "backend/generated/$target") --namespace ("Metarill."+(Get-Culture).TextInfo.ToTitleCase($target))
 if($LASTEXITCODE -ne 0){throw '代码生成失败。'}
 }
 foreach($sync in @('sync:template-version','sync:write-schema','sync:custom-routes')){
 & npm run $sync --prefix (Join-Path $metarillRoot 'frontend/metarill_uni_admin')
 if($LASTEXITCODE -ne 0){throw '平台原页面元数据同步失败。'}
 };break
}
'initialize'{
 & $dotnet run --project (Join-Path $metarillRoot 'backend/src/Metarill.Api') -- --initialize
 if($LASTEXITCODE -ne 0){throw '显式初始化失败。'};break
}
'start'{
 $port=Get-NetTCPConnection -LocalPort 8088 -State Listen -ErrorAction SilentlyContinue
 if($port){
  $owner=Get-CimInstance Win32_Process -Filter ("ProcessId="+$port[0].OwningProcess)
  if($owner.CommandLine -notlike ("*"+$dll+"*")){throw '8088端口由其他进程占用，未停止该进程。'}
  Write-Output '已复用Metarill：http://127.0.0.1:8088/api';break
 }
 & $dotnet build (Join-Path $metarillRoot 'backend/src/Metarill.Api') --nologo
 if($LASTEXITCODE -ne 0){throw '后端构建失败。'}
 $process=Start-Process -FilePath $dotnet -ArgumentList @($dll) -WorkingDirectory $metarillRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $local 'backend.log') -RedirectStandardError (Join-Path $local 'backend.err.log') -PassThru
 @{pid=$process.Id;command=$dll}|ConvertTo-Json|Set-Content -LiteralPath $pidFile -Encoding utf8
 $ready=$false
 for($i=0;$i -lt 30;$i++){try{$response=Invoke-RestMethod 'http://127.0.0.1:8088/api/runtime/health/check';if($response.code -eq 200){$ready=$true;break}}catch{};if($process.HasExited){throw '后端启动失败，请查看.local/backend.err.log。'};Start-Sleep -Milliseconds 500}
 if(-not$ready){throw '健康检查未通过，请查看日志。'}
 Write-Output '已启动：http://127.0.0.1:8088/api；日志 scripts/dev.ps1 logs；停止 scripts/dev.ps1 stop';break
}
'stop'{
 if(-not(Test-Path -LiteralPath $pidFile)){Write-Output '没有本项目PID记录，未停止任何进程。';break}
 $record=Get-Content -LiteralPath $pidFile -Raw|ConvertFrom-Json
 $process=Get-CimInstance Win32_Process -Filter ("ProcessId="+$record.pid)
 if($process -and $process.CommandLine -notlike ("*"+$dll+"*")){throw 'PID已属于其他进程，拒绝停止。'}
 if($process){Stop-Process -Id $record.pid -ErrorAction Stop}
 Remove-Item -LiteralPath $pidFile
 Write-Output 'Metarill后端已停止。';break
}
'logs'{foreach($log in @('backend.log','backend.err.log')){$path=Join-Path $local $log;if(Test-Path $path){Get-Content -LiteralPath $path -Tail 50}};break}
'test'{
 foreach($project in Get-ChildItem (Join-Path $metarillRoot 'backend/tests') -Directory){$csproj=Join-Path $project.FullName ($project.Name+'.csproj');if(Test-Path $csproj){& $dotnet test $csproj --nologo;if($LASTEXITCODE -ne 0){throw '后端测试失败。'}}};break
}
'frontend'{
 $cli='D:/HBuilderX/cli.exe'
 if(-not(Test-Path $cli)){throw '未找到HBuilderX CLI。'}
 & $cli launch web --project (Join-Path $metarillRoot 'frontend/metarill_uni_admin') --ui true
 if($LASTEXITCODE -ne 0){throw 'HBuilderX启动失败，请核对AppID与许可依赖。'};break
}
'all'{& $PSCommandPath start;& $PSCommandPath frontend;break}
}
