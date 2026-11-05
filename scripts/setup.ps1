param([string]$PythonExe = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location $projectRoot
dotnet restore FraudShield.Clean.slnx
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed' }
Push-Location Front-end
try { npm ci; if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' } } finally { Pop-Location }
if (-not (Test-Path '.venv/Scripts/python.exe')) {
    if ($PythonExe) { & $PythonExe -m venv .venv }
    else {
        py -3.12 -m venv .venv
        if ($LASTEXITCODE -ne 0) { throw 'Install Python 3.12 or pass -PythonExe with a Python 3.12 executable.' }
    }
}
& ./.venv/Scripts/python.exe -m pip install -r ai-service/requirements.txt
if ($LASTEXITCODE -ne 0) { throw 'pip install failed' }
Write-Host 'Ready. Run scripts/start-api.cmd and scripts/start-web.cmd. AI training is optional for RulesOnly.'
