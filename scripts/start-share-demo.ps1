$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$demo = Join-Path $root 'test-results\public-demo'
$app = Join-Path $demo 'app'
$secretsPath = Join-Path $demo 'secrets.json'
$cloudflared = Join-Path $root '.tools\cloudflared.exe'
$python = Join-Path $root '.venv\Scripts\python.exe'
foreach ($path in @($secretsPath, $cloudflared, $python, (Join-Path $app 'FraudDetectionService.Api.dll'), (Join-Path $root 'ai-service\artifacts\model.joblib'))) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing required demo file: $path" }
}
foreach ($port in @(18001, 18080)) {
    if (netstat -ano -p tcp | Select-String -Pattern "^\s*TCP\s+127\.0\.0\.1:$port\s+.*LISTENING") {
        throw "Port $port is already in use. Stop the previous demo first."
    }
}
$secrets = Get-Content -LiteralPath $secretsPath -Raw | ConvertFrom-Json
$env:APP_ENV = 'production'
$env:AI_SERVICE_KEY = $secrets.AiKey
$env:OMP_NUM_THREADS = '2'
$ai = Start-Process -FilePath $python -ArgumentList @('-m','uvicorn','app:app','--host','127.0.0.1','--port','18001') -WorkingDirectory (Join-Path $root 'ai-service') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $demo 'ai.out.log') -RedirectStandardError (Join-Path $demo 'ai.err.log') -PassThru

$ready = $false
for ($i = 0; $i -lt 30; $i++) {
    try {
        $health = Invoke-RestMethod 'http://127.0.0.1:18001/health' -TimeoutSec 2
        if ($health.status -eq 'ready') { $ready = $true; break }
    } catch {}
    Start-Sleep -Seconds 1
}
if (-not $ready) { throw 'AI did not become ready. See test-results/public-demo/ai.err.log.' }

$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ConnectionStrings__FraudShield = "Data Source=$demo\fraudshield.db"
$env:AllowedHosts = '*'
$env:Security__JwtSecret = $secrets.JwtSecret
$env:Security__PiiSalt = $secrets.PiiSalt
$env:Security__AdminPassword = $secrets.AdminPassword
$env:Security__AnalystPassword = $secrets.AnalystPassword
$env:Ai__BaseUrl = 'http://127.0.0.1:18001/'
$env:Ai__ApiKey = $secrets.AiKey
$api = Start-Process -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList @('FraudDetectionService.Api.dll','--urls','http://127.0.0.1:18080') -WorkingDirectory $app -WindowStyle Hidden -RedirectStandardOutput (Join-Path $demo 'api.out.log') -RedirectStandardError (Join-Path $demo 'api.err.log') -PassThru

$healthy = $false
for ($i = 0; $i -lt 30; $i++) {
    try {
        $health = Invoke-RestMethod 'http://127.0.0.1:18080/health' -TimeoutSec 2
        if ($health.status -eq 'healthy') { $healthy = $true; break }
    } catch {}
    Start-Sleep -Seconds 1
}
if (-not $healthy) { throw 'API did not become healthy. See test-results/public-demo/api.err.log.' }

$tunnelLog = Join-Path $demo 'tunnel.err.log'
if (Test-Path -LiteralPath $tunnelLog) { Clear-Content -LiteralPath $tunnelLog }
$tunnel = Start-Process -FilePath $cloudflared -ArgumentList @('tunnel','--url','http://127.0.0.1:18080') -WorkingDirectory $root -WindowStyle Hidden -RedirectStandardOutput (Join-Path $demo 'tunnel.out.log') -RedirectStandardError $tunnelLog -PassThru
$runtime = @{ AiLauncherPid = $ai.Id; ApiPid = $api.Id; TunnelPid = $tunnel.Id; StartedAt = (Get-Date).ToString('o') }
$runtime | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $demo 'runtime.json') -Encoding utf8

$url = $null
for ($i = 0; $i -lt 45; $i++) {
    if (Test-Path -LiteralPath $tunnelLog) {
        $log = Get-Content -LiteralPath $tunnelLog -Raw
        if ($log -match 'https://[a-z0-9-]+\.trycloudflare\.com') { $url = $Matches[0]; break }
    }
    Start-Sleep -Seconds 1
}
if (-not $url) { throw 'Tunnel did not return a URL. See test-results/public-demo/tunnel.err.log.' }
$url | Set-Content -LiteralPath (Join-Path $demo 'url.txt') -Encoding utf8
Write-Output "Demo URL: $url"
Write-Output 'Share the analyst username and password from test-results/public-demo/secrets.json privately.'
