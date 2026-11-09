$ErrorActionPreference = 'Stop'
$alertRoot = Split-Path -Parent $PSScriptRoot
$alertDemo = Join-Path $alertRoot 'test-results\alert-demo'
$alertApp = Join-Path $alertDemo 'app'
$alertUrl = 'http://127.0.0.1:18081'
$alertRuntime = Join-Path $alertDemo 'runtime.json'

if (Test-Path -LiteralPath $alertRuntime) {
    $previous = Get-Content -LiteralPath $alertRuntime -Raw | ConvertFrom-Json
    if (Get-Process -Id $previous.ApiPid -ErrorAction SilentlyContinue) {
        try {
            $health = Invoke-RestMethod "$alertUrl/health" -TimeoutSec 2
            if ($health.status -eq 'healthy') { Write-Output "Alert demo: $alertUrl"; exit 0 }
        } catch {}
        throw 'Previous demo process still exists but is not healthy; inspect test-results/alert-demo logs.'
    }
}
if (netstat -ano -p tcp | Select-String -Pattern '^\s*TCP\s+\S+:18081\s+.*LISTENING') {
    throw 'Port 18081 is in use; this script will not stop another service.'
}
$alertFrontend = Join-Path $alertRoot 'Front-end\dist'
if (-not (Test-Path -LiteralPath (Join-Path $alertFrontend 'index.html'))) { throw 'Run npm run build in Front-end first.' }
New-Item -ItemType Directory -Path $alertApp -Force | Out-Null
$alertProject = Join-Path $alertRoot 'Back-end\src\Services\FraudDetectionService\FraudDetectionService.Api'
dotnet publish $alertProject --no-build --no-restore -c Debug -o $alertApp
if ($LASTEXITCODE -ne 0) { throw 'Publish failed. Build the solution first.' }
$alertWeb = Join-Path $alertApp 'wwwroot'
New-Item -ItemType Directory -Path $alertWeb -Force | Out-Null
Copy-Item -Path (Join-Path $alertFrontend '*') -Destination $alertWeb -Recurse -Force

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__FraudShield = "Data Source=$alertDemo\alerts-demo.db"
$env:Security__AdminPassword = 'Admin@123456'
$env:Security__AnalystPassword = 'Analyst@123456'
$env:Ai__BaseUrl = 'http://127.0.0.1:1/'
$env:Ai__ApiKey = 'local-ai-service-key'
$alertAiSecrets = Join-Path $alertRoot 'test-results\public-demo\secrets.json'
if (Test-Path -LiteralPath $alertAiSecrets) {
    $env:Ai__BaseUrl = 'http://127.0.0.1:18001/'
    $env:Ai__ApiKey = (Get-Content -LiteralPath $alertAiSecrets -Raw | ConvertFrom-Json).AiKey
}
$alertProcess = Start-Process -FilePath (Get-Command dotnet.exe).Source -ArgumentList @('FraudDetectionService.Api.dll', '--urls', $alertUrl) -WorkingDirectory $alertApp -WindowStyle Hidden -RedirectStandardOutput (Join-Path $alertDemo 'api.out.log') -RedirectStandardError (Join-Path $alertDemo 'api.err.log') -PassThru
@{ ApiPid = $alertProcess.Id; Url = $alertUrl; App = $alertApp; StartedAt = (Get-Date).ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath $alertRuntime -Encoding utf8

$alertReady = $false
for ($attempt = 0; $attempt -lt 40; $attempt++) {
    if ($alertProcess.HasExited) { throw 'Demo exited; inspect test-results/alert-demo/api.err.log.' }
    try {
        $health = Invoke-RestMethod "$alertUrl/health" -TimeoutSec 2
        if ($health.status -eq 'healthy') { $alertReady = $true; break }
    } catch {}
    Start-Sleep -Milliseconds 500
}
if (-not $alertReady) { throw 'Demo did not become ready; inspect test-results/alert-demo logs.' }
$alertLogin = Invoke-RestMethod "$alertUrl/api/auth/login" -Method Post -ContentType 'application/json' -Body (@{ username = 'admin'; password = 'Admin@123456' } | ConvertTo-Json)
$alertHeaders = @{ Authorization = 'Bearer ' + $alertLogin.token }
$alertTransactions = Invoke-RestMethod "$alertUrl/api/transactions?size=1" -Headers $alertHeaders
if ($alertTransactions.total -eq 0) {
    $seed = Invoke-RestMethod "$alertUrl/api/demo" -Method Post -ContentType 'application/json' -Headers $alertHeaders -Body '{"mode":"RulesOnly"}'
    Write-Output "Seeded $($seed.count) simulated transactions."
}
$alertCounts = Invoke-RestMethod "$alertUrl/api/alerts/stats" -Headers $alertHeaders
Write-Output "Alert demo: $alertUrl"
Write-Output "Open alerts: $($alertCounts.openAlerts)"
Write-Output 'Local demo accounts: admin / Admin@123456; analyst / Analyst@123456'
