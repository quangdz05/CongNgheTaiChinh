[CmdletBinding(SupportsShouldProcess = $true)]
param()

$root = Split-Path -Parent $PSScriptRoot
$runtimePath = Join-Path $root 'test-results\public-demo\runtime.json'
if (-not (Test-Path -LiteralPath $runtimePath)) {
    throw 'Missing demo process information in test-results/public-demo/runtime.json.'
}
$runtime = Get-Content -LiteralPath $runtimePath -Raw | ConvertFrom-Json
$targets = @(
    @{ Id = $runtime.TunnelPid; Name = 'Cloudflare tunnel'; Command = 'tunnel --url http://127.0.0.1:18080' },
    @{ Id = $runtime.ApiPid; Name = 'FraudShield API'; Command = 'FraudDetectionService.Api.dll --urls http://127.0.0.1:18080' },
    @{ Id = $runtime.AiLauncherPid; Name = 'FraudShield AI'; Command = '-m uvicorn app:app --host 127.0.0.1 --port 18001' }
)
foreach ($target in $targets) {
    if (-not $target.Id) { continue }
    $process = Get-CimInstance Win32_Process -Filter "ProcessId = $($target.Id)" -ErrorAction SilentlyContinue
    if (-not $process) {
        Write-Output "$($target.Name) is already stopped."
        continue
    }
    if (-not $process.CommandLine.Contains($target.Command)) {
        Write-Warning "Skipping PID $($target.Id): command line does not match this demo."
        continue
    }
    if ($target.Name -eq 'FraudShield AI') {
        $children = Get-CimInstance Win32_Process -Filter "ParentProcessId = $($target.Id)" -ErrorAction SilentlyContinue |
            Where-Object { $_.CommandLine -and $_.CommandLine.Contains($target.Command) }
        foreach ($child in $children) {
            if ($PSCmdlet.ShouldProcess("PID $($child.ProcessId)", 'Stop AI worker')) {
                Stop-Process -Id $child.ProcessId -Force -ErrorAction SilentlyContinue
            }
        }
    }
    if ($PSCmdlet.ShouldProcess("PID $($target.Id)", "Stop $($target.Name)")) {
        Stop-Process -Id $target.Id -Force -ErrorAction SilentlyContinue
        Write-Output "Stopped $($target.Name)."
    }
}
