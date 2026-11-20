#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('frontend', 'backend', 'all')]
    [string]$Part = 'all',
    [string]$Commit = 'HEAD',
    [string]$HBuilderX = 'D:/HBuilderX/cli.exe',
    [ValidatePattern('\Ametarill(-dev-check-[a-f0-9]{8})?\z')]
    [string]$Instance = 'metarill',
    [ValidateRange(1024, 65535)]
    [int]$Port = 18088,
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$Part = $Part.ToLowerInvariant()
$Instance = $Instance.ToLowerInvariant()
if ($Instance -eq 'metarill' -and $Port -ne 18088) { throw 'Metarill 开发实例的端口固定为 18088。' }
$server = 'deve-server.yinzon.com'
$sshOptions = @('-o', 'BatchMode=yes', '-o', 'StrictHostKeyChecking=yes', '-o', 'ConnectTimeout=10')
$remoteScript = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'deploy-server200.sh')
$root = Split-Path -Parent $PSScriptRoot

# 远端参数只使用固定值、已验证的实例和端口、或本机生成的十六进制摘要。
function Invoke-ServerDeploy([string]$Action, [string[]]$Arguments = @()) {
    $command = "tr -d '\r' | bash -s -- $Action $Instance $Port $Part $($Arguments -join ' ')"
    $remoteScript | & ssh @sshOptions $server $command
    if ($LASTEXITCODE -ne 0) { throw "Server-200 部署步骤 $Action 失败（退出码 $LASTEXITCODE）。" }
}

function Invoke-Tool([string]$Executable, [string[]]$Arguments, [string]$FailureMessage) {
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$FailureMessage（退出码 $LASTEXITCODE）。" }
}

Invoke-ServerDeploy check
if ($Check) { return }

$buildBackend = $Part -ne 'frontend'
$buildFrontend = $Part -ne 'backend'
$revision = & git -C $root rev-parse --verify --end-of-options "$Commit^{commit}"
if ($LASTEXITCODE -ne 0 -or $revision -cnotmatch '\A[a-f0-9]{40}\z') { throw '无法解析要部署的 Git 提交。' }
Write-Host "正在构建 Git 提交 $revision，更新范围 $Part。"
$runId = [guid]::NewGuid().ToString('N')
$scratchParent = Join-Path $root '.local/server200-deploy'
$scratch = Join-Path $scratchParent $runId
$payload = Join-Path $scratch 'payload'
$source = Join-Path $scratch 'source'
$uploaded = $false

try {
    New-Item -ItemType Directory -Path $payload, $source -Force | Out-Null
    $snapshot = Join-Path $scratch 'source.tar'
    # 在线生成需要同一提交的完整源码布局。只导出 Git 对象，避免带上本机私有配置和未提交改动。
    $sourcePaths = @()
    if (-not $buildBackend) { $sourcePaths = @('frontend/metarill_uni_admin') }
    Invoke-Tool git (@('-C', $root, 'archive', '--format=tar', "--output=$snapshot", $revision, '--') + $sourcePaths) '导出源码失败'
    Invoke-Tool tar @('-xf', $snapshot, '-C', $source) '解包源码失败'

    if ($buildBackend) {
        $backend = Join-Path $payload 'backend'
        New-Item -ItemType Directory -Path $backend | Out-Null
        $dotnet = Join-Path $root '.tools/dotnet/dotnet.exe'
        if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
        Push-Location -LiteralPath $source
        try {
            Invoke-Tool $dotnet @('publish', 'backend/src/Metarill.Api/Metarill.Api.csproj', '-c', 'Release',
                '-r', 'linux-x64', '--self-contained', 'false', '--nologo', '-o', (Join-Path $backend 'host')) 'C# 后端发布失败'
        } finally { Pop-Location }
        # 保留编译前的 Git 归档，避免将编译产生的 obj/bin 混入在线生成的源码。
        Move-Item -LiteralPath $snapshot -Destination (Join-Path $backend 'source.tar')
    }

    if ($buildFrontend) {
        if (-not (Test-Path -LiteralPath $HBuilderX -PathType Leaf)) { throw '找不到 HBuilderX CLI，请用 -HBuilderX 指定 cli.exe。' }
        $frontend = Join-Path $source 'frontend/metarill_uni_admin'
        Invoke-Tool npm @('ci', '--ignore-scripts', '--prefer-offline', '--prefix', $frontend) '前端依赖安装失败'
        & $HBuilderX open
        $ready = $false
        for ($attempt = 0; $attempt -lt 10; $attempt++) {
            $version = & $HBuilderX version | Out-String
            if ($version -match '\d+\.\d+\.\d{6,}') { $ready = $true; break }
            Start-Sleep -Seconds 1
        }
        if (-not $ready) { throw 'HBuilderX CLI 未就绪，请在当前 Windows 桌面启动 HBuilderX 后重试。' }
        try {
            Invoke-Tool $HBuilderX @('project', 'open', '--path', $frontend) '导入临时 HBuilderX 工程失败'
            $publishLines = & $HBuilderX publish web --project $frontend --webTitle Metarill --webHosting false
            $hbxExit = $LASTEXITCODE
            $publishLines | Write-Host
            $web = Join-Path $frontend 'unpackage/dist/build/web'
            if ($hbxExit -ne 0 -or ($publishLines -join "`n") -notmatch '导出Web成功' -or
                -not (Test-Path -LiteralPath (Join-Path $web 'index.html') -PathType Leaf)) {
                throw 'HBuilderX Web 发行失败，请查看上面的构建日志。'
            }
            Move-Item -LiteralPath $web -Destination (Join-Path $payload 'web')
        } finally {
            & $HBuilderX project close --path $frontend | Out-Host
        }
    }

    $archive = Join-Path $scratch 'deploy.tar.gz'
    Invoke-Tool tar @('-czf', $archive, '-C', $payload, '.') '打包更新产物失败'
    $checksum = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    $uploaded = $true
    Invoke-Tool scp ($sshOptions + @($archive, "${server}:/tmp/metarill-upload-$runId.tar.gz")) '上传 Server-200 失败'
    Invoke-ServerDeploy apply @($runId, $checksum, $revision)
    $uploaded = $false
} finally {
    if ($uploaded) {
        try { Invoke-ServerDeploy cleanup @($runId) }
        catch { Write-Warning "服务器临时上传文件清理失败：/tmp/metarill-upload-$runId.tar.gz" }
    }
    if (Test-Path -LiteralPath $scratch) {
        $resolvedScratch = (Resolve-Path -LiteralPath $scratch).ProviderPath
        if ((Split-Path -Parent $resolvedScratch) -eq $scratchParent -and (Split-Path -Leaf $resolvedScratch) -ceq $runId) {
            Remove-Item -LiteralPath $resolvedScratch -Recurse -Force
        }
    }
}
