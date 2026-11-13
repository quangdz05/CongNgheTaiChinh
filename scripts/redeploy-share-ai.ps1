$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$demo = Join-Path $root 'test-results\public-demo'
$runtimePath = Join-Path $demo 'runtime.json'
$runtime = Get-Content -LiteralPath $runtimePath -Raw | ConvertFrom-Json
$secrets = Get-Content -LiteralPath (Join-Path $demo 'secrets.json') -Raw | ConvertFrom-Json
$expected = '-m uvicorn app:app --host 127.0.0.1 --port 18001'
$launcher = Get-CimInstance Win32_Process -Filter "ProcessId = $($runtime.AiLauncherPid)" -ErrorAction SilentlyContinue
if ($launcher) {
    if (-not $launcher.CommandLine.Contains($expected)) { throw 'Recorded AI process does not match the demo.' }
    $children = Get-CimInstance Win32_Process -Filter "ParentProcessId = $($launcher.ProcessId)" |
        Where-Object { $_.CommandLine -and $_.CommandLine.Contains($expected) }
    foreach ($child in $children) { Stop-Process -Id $child.ProcessId -Force }
    Stop-Process -Id $launcher.ProcessId -Force -ErrorAction SilentlyContinue
}
$env:APP_ENV = 'production'
$env:AI_SERVICE_KEY = $secrets.AiKey
$env:OMP_NUM_THREADS = '2'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$ai = Start-Process -FilePath (Join-Path $root '.venv\Scripts\python.exe') -ArgumentList @('-m','uvicorn','app:app','--host','127.0.0.1','--port','18001') -WorkingDirectory (Join-Path $root 'ai-service') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $demo "ai-$stamp.out.log") -RedirectStandardError (Join-Path $demo "ai-$stamp.err.log") -PassThru
$runtime.AiLauncherPid = $ai.Id
$runtime | ConvertTo-Json | Set-Content -LiteralPath $runtimePath -Encoding utf8
for ($i = 0; $i -lt 40; $i++) {
    if ($ai.HasExited) { throw "AI exited. Inspect ai-$stamp.err.log." }
    try {
        $health = Invoke-RestMethod 'http://127.0.0.1:18001/health' -TimeoutSec 2
        $ulb = Invoke-RestMethod 'http://127.0.0.1:18001/v1/workspace/ulb' -Headers @{ 'X-Service-Key' = $secrets.AiKey } -TimeoutSec 5
        if ($health.status -eq 'ready' -and $ulb.featureSchemaVersion -eq 'ulb-pca-v1') {
            Write-Output "AI ready. ULB model: $($ulb.modelVersion)"
            exit 0
        }
    } catch {}
    Start-Sleep -Milliseconds 500
}
throw "AI did not become ready. Inspect ai-$stamp.err.log."
