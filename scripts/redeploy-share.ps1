$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$demo = [IO.Path]::GetFullPath((Join-Path $root 'test-results\public-demo'))
$app = Join-Path $demo 'app'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$stage = Join-Path $demo "app-staging-$stamp"
$previous = Join-Path $demo "app-previous-$stamp"
$runtimePath = Join-Path $demo 'runtime.json'
$python = Join-Path $root '.venv\Scripts\python.exe'

function Assert-DemoPath([string]$path) {
    $resolved = [IO.Path]::GetFullPath($path)
    if (-not $resolved.StartsWith($demo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Deployment path is outside the demo directory: $resolved"
    }
}
foreach ($path in @($app, $stage, $previous)) { Assert-DemoPath $path }
if ((Test-Path -LiteralPath $stage) -or (Test-Path -LiteralPath $previous)) { throw 'Deployment directory already exists. Retry after one second.' }
$runtime = Get-Content -LiteralPath $runtimePath -Raw | ConvertFrom-Json
$apiProcess = Get-CimInstance Win32_Process -Filter "ProcessId = $($runtime.ApiPid)"
if (-not $apiProcess -or -not $apiProcess.CommandLine.Contains('FraudDetectionService.Api.dll --urls http://127.0.0.1:18080')) {
    throw 'The recorded API process does not match this demo. Start the demo before redeploying.'
}
$aiHealth = Invoke-RestMethod 'http://127.0.0.1:18001/health' -TimeoutSec 5
if ($aiHealth.status -ne 'ready') { throw 'AI is not ready.' }

Push-Location (Join-Path $root 'Front-end')
try {
    & npm.cmd run build
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
} finally { Pop-Location }
& dotnet publish (Join-Path $root 'Back-end\src\Services\FraudDetectionService\FraudDetectionService.Api') -c Release -o $stage --no-restore --disable-build-servers -m:1
if ($LASTEXITCODE -ne 0) { throw 'API publish failed.' }
Copy-Item -LiteralPath (Join-Path $root 'Front-end\dist') -Destination (Join-Path $stage 'wwwroot') -Recurse

$backupDir = Join-Path $demo 'backups'
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
$backup = Join-Path $backupDir "fraudshield-$stamp.db"
Assert-DemoPath $backup
$backupCode = @'
import sqlite3, sys
with sqlite3.connect(sys.argv[1]) as source, sqlite3.connect(sys.argv[2]) as destination:
    source.backup(destination)
    assert destination.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
'@
& $python -c $backupCode (Join-Path $demo 'fraudshield.db') $backup
if ($LASTEXITCODE -ne 0) { throw 'Database backup failed.' }

$secrets = Get-Content -LiteralPath (Join-Path $demo 'secrets.json') -Raw | ConvertFrom-Json
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ConnectionStrings__FraudShield = "Data Source=$demo\fraudshield.db"
$env:AllowedHosts = '*'
$env:Proxy__ForwardLimit = '1'
$env:Security__JwtSecret = $secrets.JwtSecret
$env:Security__PiiSalt = $secrets.PiiSalt
$env:Security__AdminPassword = $secrets.AdminPassword
$env:Security__AnalystPassword = $secrets.AnalystPassword
$env:Ai__BaseUrl = 'http://127.0.0.1:18001/'
$env:Ai__ApiKey = $secrets.AiKey

function Start-DemoApi {
    $started = Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList @('FraudDetectionService.Api.dll','--urls','http://127.0.0.1:18080') -WorkingDirectory $app -WindowStyle Hidden -RedirectStandardOutput (Join-Path $demo 'api.out.log') -RedirectStandardError (Join-Path $demo 'api.err.log') -PassThru
    for ($i = 0; $i -lt 40; $i++) {
        if ($started.HasExited) { throw 'API exited during startup. Inspect api.out.log and api.err.log.' }
        try {
            $health = Invoke-RestMethod 'http://127.0.0.1:18080/health' -TimeoutSec 2
            if ($health.status -eq 'healthy') { return $started }
        } catch {}
        Start-Sleep -Milliseconds 500
    }
    Stop-Process -Id $started.Id -Force -ErrorAction SilentlyContinue
    throw 'API did not become healthy.'
}

# The AI service and Cloudflare tunnel keep running, preserving the current URL.
Stop-Process -Id $apiProcess.ProcessId -Force
Wait-Process -Id $apiProcess.ProcessId -Timeout 15 -ErrorAction SilentlyContinue
Move-Item -LiteralPath $app -Destination $previous
try {
    Move-Item -LiteralPath $stage -Destination $app
    $api = Start-DemoApi
} catch {
    $failure = $_
    $failed = Join-Path $demo "app-failed-$stamp"
    Assert-DemoPath $failed
    if (Test-Path -LiteralPath $app) { Move-Item -LiteralPath $app -Destination $failed }
    Move-Item -LiteralPath $previous -Destination $app
    $api = Start-DemoApi
    $runtime.ApiPid = $api.Id
    $runtime | ConvertTo-Json | Set-Content -LiteralPath $runtimePath -Encoding utf8
    throw "Deployment failed; previous app restored. $failure"
}
$runtime.ApiPid = $api.Id
$runtime | Add-Member -NotePropertyName RedeployedAt -NotePropertyValue (Get-Date).ToString('o') -Force
$runtime | ConvertTo-Json | Set-Content -LiteralPath $runtimePath -Encoding utf8
Write-Output "Deployment healthy. Backup: $backup"
Write-Output "Demo URL: $((Get-Content -LiteralPath (Join-Path $demo 'url.txt') -Raw).Trim())"
