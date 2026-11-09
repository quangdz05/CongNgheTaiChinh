$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$frontend = Join-Path $root 'Front-end'
$dist = Join-Path $frontend 'dist'
$wwwroot = Join-Path $root 'test-results\public-demo\app\wwwroot'
if (-not (Test-Path -LiteralPath $wwwroot)) {
    throw 'The temporary demo has not been prepared. Missing test-results/public-demo/app/wwwroot.'
}

Push-Location $frontend
try {
    & npm.cmd run build
    if ($LASTEXITCODE -ne 0) { throw "Frontend build failed with exit code $LASTEXITCODE." }
} finally {
    Pop-Location
}

$index = Join-Path $dist 'index.html'
if (-not (Test-Path -LiteralPath $index)) { throw 'Build did not create dist/index.html.' }
$distPrefix = $dist.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
Get-ChildItem -LiteralPath $dist -File -Recurse | Where-Object { $_.FullName -ne $index } | ForEach-Object {
    $relative = $_.FullName.Substring($distPrefix.Length)
    $destination = Join-Path $wwwroot $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
}
Copy-Item -LiteralPath $index -Destination (Join-Path $wwwroot 'index.html') -Force
Write-Output 'Frontend updated. The API, AI, database, and current public URL remain in use.'
$urlPath = Join-Path $root 'test-results\public-demo\url.txt'
if (Test-Path -LiteralPath $urlPath) {
    Write-Output "Current URL: $((Get-Content -LiteralPath $urlPath -Raw).Trim())"
}
Write-Output 'Refresh the page with Ctrl+F5 to load the latest frontend.'
